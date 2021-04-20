using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Local;

namespace Stratis.Bitcoin.Features.SmartContracts.SystemContracts
{
    public interface ISystemContractExecutor
    {
        SystemContractExecutionResult Execute(SystemContractContext context);
    }

    public class SystemContractLocalExecutorFactory
    {
        private readonly IProvenBlockHeaderProvider blockHeaderProvider;
        private readonly ILocalExecutor localExecutor;

        public SystemContractLocalExecutorFactory(ILoggerFactory logger, IProvenBlockHeaderProvider blockHeaderProvider)
        {
            this.blockHeaderProvider = blockHeaderProvider;
        }

        /// <summary>
        /// Creates a local executor with the correct context for the system contract execution.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public ILocalExecutor GetLocalExecutor(SystemContractContext context)
        {
            return null;
        }
    }

    public class SystemContractExecutor : ISystemContractExecutor
    {
        private readonly IProvenBlockHeaderProvider blockHeaderProvider;
        private readonly ILocalExecutor localExecutor;

        public SystemContractExecutor(ILoggerFactory logger, IProvenBlockHeaderProvider blockHeaderProvider, ILocalExecutor localExecutor)
        {
            this.blockHeaderProvider = blockHeaderProvider;
            this.localExecutor = localExecutor;
        }

        public SystemContractExecutionResult Execute(SystemContractContext context)
        {
            // Get the block header and hash state root for this height
            // Load the state root and put it into the execution context
            // Invoke the local call executor?
            var result = this.localExecutor.Execute((ulong)context.BlockHeight, null, Money.Zero, context.TxData);

            return new SystemContractExecutionResult(result.Return);
        }
    }
}
