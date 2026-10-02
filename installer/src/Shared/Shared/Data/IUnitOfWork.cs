using System.Threading;
using System.Threading.Tasks;

namespace Shared.Data;

public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

