using System;

namespace RIMAPI.Core
{
    public interface IServiceProvider
    {
        T GetService<T>();
        object GetService(Type type);
        bool TryGetService<T>(out T service);
    }
}
