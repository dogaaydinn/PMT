using System.Text.Json;
using AutoMapper;
using Business.Constants.Messages.Services.Communication;
using Business.Services.Auth.Abstract;
using Business.Services.Communication.Abstract;
using Core.ExceptionHandling;
using Core.Security.SessionManagement;
using Core.Services.Messages;
using Core.Services.Payload;
using Core.Services.Result;
using Core.Utils.Hashing;
using Core.Utils.IoC;
using Core.Utils.Rules;
using DataAccess.Repositories.Abstract.UserManagement;
using Domain.DTOs.Auth;
using Domain.DTOs.DutyManagement.UserManagement;
using Domain.Entities.DutyManagement.UserManagement;

namespace Business.Services.Auth.Concrete;

public class AuthManager: IAuthService
{
    private readonly IUserDal _userDal = ServiceTool.GetService<IUserDal>()!;
    private readonly ITokenHandler _tokenHandler = ServiceTool.GetService<ITokenHandler>()!;
    private readonly IMapper _mapper = ServiceTool.GetService<IMapper>()!;
    private readonly IMailingService _emailService = ServiceTool.GetService<IMailingService>()!;

    
    //It checks if the LoginDto object is not null and if the email is valid.
    // It retrieves the user from the database using the provided email or username.
    // If the user is not found, it returns a failure result with an error message.
    // It verifies the provided password with the stored password hash and salt. If the password is incorrect, it returns a failure result with an error message.
    // If the user has enabled multi-factor authentication (MFA), it generates a verification code, sends it to the user's email, and returns a warning message indicating that MFA is required.
    // If MFA is not enabled or after the MFA code is verified, it generates a token for the user, updates the user's active token in the database, and returns a success result with the token and user information.
    
    #region login
    public async Task<ServiceObjectResult<LoginResponseDto?>> LoginAsync(LoginDto loginDto)
    {
        var result = new ServiceObjectResult<LoginResponseDto?>();

        try
        {
            if (loginDto.Email != null)
                BusinessRules.Run(
                    ("AUTH-366764", BusinessRules.CheckDtoNull(loginDto)),
                    ("AUTH-584337", BusinessRules.CheckEmail(loginDto.Email)));

            var user = await _userDal.GetAsync(
                u => u.Email == loginDto.Email || u.Username == loginDto.Username);

            if (user == null)
            {
                result.Fail(new ErrorMessage("AUTH-809431", AuthServiceMessages.NotFound));
                return result;
            }

            if (!HashingHelper.VerifyPasswordHash(loginDto.Password, user.PasswordHash, user.PasswordSalt))
            {
                result.Fail(new ErrorMessage("AUTH-290694", AuthServiceMessages.WrongPassword));
                return result;
            }

            if (user.UseMultiFactorAuthentication)
            {
                user.LoginVerificationCode = GenerateMfaCode();
                user.LoginVerificationCodeExpiration = DateTime.Now.AddMinutes(1);
                user.LastLoginTime = DateTime.Now;
                await _userDal.UpdateAsync(user);
                var emailMessage =
                    $"Please use this code to login: {user.LoginVerificationCode}. The code will expire in 1 minute.";
                var mailResult = _emailService.SendSmtp(user.Email, "Verify Login", emailMessage);

                if (mailResult.HasFailed)
                {
                    var errDescription = mailResult.Messages[0].Description;
                    result.Fail(new ErrorMessage(mailResult.ResultCode!,
                        errDescription ?? AuthServiceMessages.VerificationCodeMailNotSent));

                    return result;
                }

                result.ExtraData.Add(new ServicePayloadItem("useMFA", true));
                result.Warning(AuthServiceMessages.MfaRequired);
                return result;
            }

            var token = _tokenHandler.GenerateToken(user.Id.ToString(), user.Username, user.Email, user.Role, false);

            var serializedToken = JsonSerializer.Serialize(token);
            user.ActiveToken = serializedToken;
            await _userDal.UpdateAsync(user);

            var userGetDto = _mapper.Map<UserGetDto>(user);
            result.SetData(new LoginResponseDto { Token = token!, User = userGetDto },
                AuthServiceMessages.LoginSuccessful);
        }
        catch (ValidationException ex)
        {
            result.Fail(new ErrorMessage(ex.ExceptionCode, ex.Message));
        }
        catch (Exception ex)
        {
            result.Fail(new ErrorMessage("AUTH-347466", ex.Message));
        }

        return result;
    }
    #endregion
    
    //It maps the registerDto object to a User object.
    // It checks if a user with the same username or email already exists in the database.
    // If such a user exists, it returns a failure result with an error message.
    // If no such user exists, it hashes the provided password and sets the hash and salt to the User object.
    // It sets some additional properties on the User object, such as Role, CreatedAt, CreatedUserId, and IsDeleted.
    // It adds the new user to the database.
    // It generates a token for the new user.
    // It maps the User object to a UserGetDto object.
    // It sets the token and the UserGetDto object to a LoginResponseDto object and returns a success result with this data.

    #region register
    public async Task<ServiceObjectResult<LoginResponseDto?>> RegisterAsync(RegisterDto registerDto)
    {
        var result = new ServiceObjectResult<LoginResponseDto?>();

        try
        {
            var user = _mapper.Map<User>(registerDto);
            var isUserExist = await _userDal.GetAsync(x => x.Username == user.Username || x.Email == user.Email);

            if (isUserExist != null)
            {
                result.Fail(new ErrorMessage("AUTH-432894", "User already exists"));
                return result;
            }
            
            HashingHelper.CreatePasswordHash(registerDto.Password, out var passwordHash, out var passwordSalt);
            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            user.Role = "User";
            user.CreatedAt = DateTime.Now;
            user.CreatedUserId = Guid.Empty;
            user.IsDeleted = false;
            
            await _userDal.AddAsync(user);
            var token = _tokenHandler.GenerateToken(user.Id.ToString(), user.Username, user.Email, user.Role, false);
            var userGetDto = _mapper.Map<UserGetDto>(user);
            
            var loginResponseDto = new LoginResponseDto
            {
                Token = token,
                User = userGetDto
            };
            
            result.SetData(loginResponseDto);
        }
        catch (ValidationException e)
        {
            result.Fail(e);
        }
        catch (Exception e)
        {
            result.Fail(new ErrorMessage("AUTH-943056", e.Message));
        }
        
        return result;
    }
    #endregion

    #region reset-password
    public async Task<ServiceObjectResult<LoginResponseDto?>> ResetPasswordAsync(ResetPasswordDto resetPasswordDto)
    {
        var result = new ServiceObjectResult<LoginResponseDto?>();

        try
        {
            var user = await _userDal.GetAsync(x => x.Email == resetPasswordDto.Email);

            if (user == null)
            {
                result.Fail(new ErrorMessage("AUTH-432894", "User not found"));
                return result;
            }

            if (user.ResetPasswordCode != resetPasswordDto.LoginVerificationCode)
            {
                result.Fail(new ErrorMessage("AUTH-755666", AuthServiceMessages.WrongVerificationCode));
                return result;
            }

            if (user.ResetPasswordCodeExpiration == null || user.ResetPasswordCodeExpiration < DateTime.UtcNow)
            {
                result.Fail(new ErrorMessage("AUTH-221332", AuthServiceMessages.VerificationCodeExpired));
                return result;
            }

            HashingHelper.CreatePasswordHash(resetPasswordDto.NewPassword, out var passwordHash, out var passwordSalt);
            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            user.ResetPasswordCode = null;
            user.ResetPasswordCodeExpiration = null;

            await _userDal.UpdateAsync(user);

            var token = _tokenHandler.GenerateToken(user.Id.ToString(), user.Username, user.Email, user.Role, false);

            if (token == null)
            {
                result.Fail(new ErrorMessage("AUTH-564321", "Failed to generate token"));
                return result;
            }

            var userGetDto = _mapper.Map<UserGetDto>(user);
            result.SetData(new LoginResponseDto { Token = token, User = userGetDto },
                AuthServiceMessages.PasswordResetSuccessful);
        }
        catch (ValidationException ex)
        {
            result.Fail(new ErrorMessage(ex.ExceptionCode, ex.Message));
        }
        catch (Exception ex)
        {
            result.Fail(new ErrorMessage("AUTH-562594", "An unexpected error occurred: " + ex.Message));
        }

        return result;
    }
    #endregion
    
    //method is used when a user has already received a verification code (possibly from the VerifyEmailAsync method) and is entering it to verify their email.
    //This method checks if the entered code matches the one stored in the database and if the code has not expired.
    
    #region verify-email-code
    public async Task<ServiceObjectResult<LoginResponseDto?>> VerifyEmailCodeAsync(VerifyEmailCodeDto verifyCodeDto)
    {
        var result = new ServiceObjectResult<LoginResponseDto?>();
        try
        {
            var user = await _userDal.GetAsync(p => p.Email == verifyCodeDto.Email);
            if (user == null)
            {
                result.Fail(new ErrorMessage("AUTH-808079", AuthServiceMessages.NotFound));
                return result;
            }

            if (user.LoginVerificationCode != verifyCodeDto.Code)
            {
                result.Fail(new ErrorMessage("AUTH-755666", AuthServiceMessages.WrongVerificationCode));
                return result;
            }

            if (user.LoginVerificationCodeExpiration < DateTime.UtcNow)
            {
                result.Fail(new ErrorMessage("AUTH-221332", AuthServiceMessages.VerificationCodeExpired));
                return result;
            }

            user.LoginVerificationCode = null;
            user.LoginVerificationCodeExpiration = null;

            await _userDal.UpdateAsync(user);

            var token = _tokenHandler.GenerateToken(user.Id.ToString(), user.Username, user.Email, user.Role, false);

            var userGetDto = _mapper.Map<UserGetDto>(user);
            result.SetData(new LoginResponseDto { Token = token!, User = userGetDto },
                AuthServiceMessages.VerificationSuccessful);
        }
        catch (ValidationException ex)
        {
            result.Fail(new ErrorMessage(ex.ExceptionCode, ex.Message));
        }
        catch (Exception ex)
        {
            result.Fail(new ErrorMessage("AUTH-562594", ex.Message));
        }

        return result;
    }
    #endregion

    //It retrieves the user from the database using the email provided in the verifyMfaCodeDto object.
    // If no user with the provided email is found, it returns a failure result with an error message.
    // It checks if the MFA code provided by the user matches the one stored in the database for the user. If the codes don't match, it returns a failure result with an error message.
    // It checks if the MFA code has expired. If the code has expired, it returns a failure result with an error message.
    // If the MFA code is valid and has not expired, it clears the MFA code and its expiration date from the user's record in the database.
    // It generates a token for the user.
    // It maps the User object to a UserGetDto object.
    // It sets the token and the UserGetDto object to a LoginResponseDto object and returns a success result with this data.

    #region verify-mfa

    public async Task<ServiceObjectResult<LoginResponseDto?>> VerifyMfaCodeAsync(VerifyMfaCodeDto verifyMfaCodeDto)
    {
        var result = new ServiceObjectResult<LoginResponseDto?>();
        try
        {
            var user = await _userDal.GetAsync(p => p.Email == verifyMfaCodeDto.Email);
            if (user == null)
            {
                result.Fail(new ErrorMessage("AUTH-808079", AuthServiceMessages.NotFound));
                return result;
            }

            if (user.LoginVerificationCode != verifyMfaCodeDto.Code)
            {
                result.Fail(new ErrorMessage("AUTH-755666", AuthServiceMessages.WrongVerificationCode));
                return result;
            }

            if (user.LoginVerificationCodeExpiration < DateTime.UtcNow)
            {
                result.Fail(new ErrorMessage("AUTH-221332", AuthServiceMessages.VerificationCodeExpired));
                return result;
            }

            user.LoginVerificationCode = null;
            user.LoginVerificationCodeExpiration = null;

            await _userDal.UpdateAsync(user);

            var token = _tokenHandler.GenerateToken(user.Id.ToString(), user.Username, user.Email, user.Role, false);

            var userGetDto = _mapper.Map<UserGetDto>(user);
            result.SetData(new LoginResponseDto { Token = token!, User = userGetDto },
                AuthServiceMessages.VerificationSuccessful);
        }
        catch (ValidationException ex)
        {
            result.Fail(new ErrorMessage(ex.ExceptionCode, ex.Message));
        }
        catch (Exception ex)
        {
            result.Fail(new ErrorMessage("AUTH-562594", ex.Message));
        }

        return result;
    }

    #endregion
    
    #region forgot-password
    public async Task<ServiceObjectResult<bool>> ForgotPasswordAsync(ForgotPasswordDto forgotPasswordDto)
    {
        var result = new ServiceObjectResult<bool>();

        try
        {
            var user = await _userDal.GetAsync(x => x.Email == forgotPasswordDto.Email);

            if (user == null)
            {
                result.Fail(new ErrorMessage("AUTH-432894", "User not found"));
                return result;
            }
            
            user.ResetPasswordCode = GenerateMfaCode();
            user.ResetPasswordCodeExpiration = DateTime.Now.AddMinutes(1);
            await _userDal.UpdateAsync(user);
            var emailMessage =
                $"Please use this code to reset your password: {user.ResetPasswordCode}. The code will expire in 1 minute.";
            var mailResult = _emailService.SendSmtp(user.Email, "Reset Password", emailMessage);

            if (mailResult.HasFailed)
            {
                var errDescription = mailResult.Messages[0].Description;
                result.Fail(new ErrorMessage(mailResult.ResultCode!,
                    errDescription ?? AuthServiceMessages.VerificationCodeMailNotSent));

                return result;
            }

            result.SetData(true);
        }
        catch (ValidationException e)
        {
            result.Fail(e);
        }
        catch (Exception e)
        {
            result.Fail(new ErrorMessage("AUTH-943056", e.Message));
        }
        
        return result;
    }
    #endregion
    

//method is used when a user wants to verify their email address for the first time or when they want to change their email.
//This method generates a new verification code, sends it to the user's email, and waits for the user to enter the code to verify their email.

    #region logout
    public async Task<ServiceObjectResult<bool>> LogoutAsync(LogoutDto logoutDto)
    {
        var result = new ServiceObjectResult<bool>();

        try
        {
            var user = await _userDal.GetAsync(x => x.ActiveToken == logoutDto.Token);

            if (user == null)
            {
                result.Fail(new ErrorMessage("AUTH-432894", "User not found"));
                return result;
            }
            
            user.ActiveToken = null;
            await _userDal.UpdateAsync(user);
            
            result.SetData(true);
        }
        catch (ValidationException e)
        {
            result.Fail(e);
        }
        catch (Exception e)
        {
            result.Fail(new ErrorMessage("AUTH-943056", e.Message));
        }
        
        return result;
    }
    #endregion
    private static string GenerateMfaCode()
    {
        var code = new Random().Next(100000, 999999).ToString();
        return code;
    }

    private async Task<string?> CheckIfUsernameRegisteredBefore(string username)
    {
        var user = await _userDal.GetAsync(p => p.Username == username);
        return user != null ? AuthServiceMessages.UsernameAlreadyRegistered : null;
    }

    private async Task<string?> CheckIfEmailRegisteredBefore(string email)
    {
        var user = await _userDal.GetAsync(p => p.Email == email);
        return user != null ? AuthServiceMessages.EmailAlreadyRegistered : null;
    }
    
} 

    