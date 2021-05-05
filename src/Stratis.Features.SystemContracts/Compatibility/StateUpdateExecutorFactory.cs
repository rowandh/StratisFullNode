using System.Text;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts.Compatibility
{
    /// <summary>
    /// Wrapper around the system contract executor for compatibility with existing SC execution model.
    /// </summary>
    public class StateUpdateExecutorFactory : IContractExecutorFactory
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly IStateUpdater runner;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly IWhitelistedHashChecker whitelistedHashChecker;

        public StateUpdateExecutorFactory(ILoggerFactory loggerFactory, IStateUpdater runner, ICallDataSerializer callDataSerializer, IWhitelistedHashChecker whitelistedHashChecker)
        {
            this.loggerFactory = loggerFactory;
            this.runner = runner;
            this.callDataSerializer = callDataSerializer;
            this.whitelistedHashChecker = whitelistedHashChecker;
        }

        public IContractExecutor CreateExecutor(IStateRepositoryRoot stateRepository, IContractTransactionContext transactionContext)
        {
            return new StateUpdateExecutor(this.loggerFactory, this.runner, this.callDataSerializer, this.whitelistedHashChecker, stateRepository);
        }
    }
}
