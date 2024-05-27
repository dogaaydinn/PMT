using System.ComponentModel.DataAnnotations;
using Core.Constants.Duty;
using Core.Constants.Project;
using Core.Domain.Abstract;
using Domain.Entities.DutyManagement.UserManagement;

namespace Domain.DTOs.ProjectManagement;

public class TeamCreateDto : IDto
{
    public string Name { get; set; }
    public string? Description { get; set; }
    public Guid ManagerId { get; set; } 
    public Priority Priority { get; set; }
    public ProjectStatus Status { get; set; }
}