using Core.Constants;
using Core.Constants.Duty;
using Core.Constants.Project;
using Core.Domain.Abstract;

namespace Domain.DTOs.ProjectManagement;

public class TeamUpdateDto : IDto
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public ProjectStatus? Status { get; set; }
    public Priority? Priority { get; set; }
    public Guid? ManagerId { get; set; }
}