using Core.DataAccess.EntityFramework;
using DataAccess.Context.EntityFramework;
using DataAccess.Repositories.Abstract.TaskManagement;
using Domain.Entities.DutyManagement;

namespace DataAccess.Repositories.Concrete.EntityFramework.TaskManagement;

public class EfDutyDal : EfEntityRepository<Duty, EfDbContext>, IDutyDal
{
}