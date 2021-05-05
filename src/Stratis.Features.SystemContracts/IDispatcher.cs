using CSharpFunctionalExtensions;
using Stratis.SmartContracts.CLR;

namespace Stratis.Features.SystemContracts
{
    public interface IDispatcher<T> : IDispatcher
    {
        T GetInstance(IStateUpdateContext context);
    }

    public interface IDispatcher
    {
        Result<object> Dispatch(IStateUpdateContext context);
        EmbeddedContractIdentifier Identifier { get; }
    }

    public static class DispatchResult
    {
        /// <summary>
        /// C# functional extensions doesn't support returning nulls with a Result<T> class, so we use
        /// <see cref="Void"/> to return from void methods.
        /// </summary>
        public static object Void = new object();
    }
}