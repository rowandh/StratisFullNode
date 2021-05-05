using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface IStateUpdateResult
    {
        IStateRepositoryRoot NewState { get; }

        object Result { get; }
    }

    public class StateUpdateResult : IStateUpdateResult
    {
        public StateUpdateResult(IStateRepositoryRoot newState)
        {
            this.NewState = newState;
        }

        public StateUpdateResult(IStateRepositoryRoot newState, object result)
        {
            this.NewState = newState;
            this.Result = result;
        }

        public IStateRepositoryRoot NewState { get; }

        public object Result { get; }
    }
}
