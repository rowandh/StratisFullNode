using System;
using System.Linq;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts.Compatibility
{
    /// <summary>
    /// Wrapper around the system contract runner for compatibility with existing SC execution model.
    /// </summary>
    public class StateUpdateExecutor : IContractExecutor
    {
        private readonly IStateUpdater runner;
        private readonly IStateRepositoryRoot stateRepository;
        private readonly IWhitelistedHashChecker whitelistedHashChecker;
        private readonly ICallDataSerializer callDataSerializer;
        private ILogger logger;

        public StateUpdateExecutor(ILoggerFactory loggerFactory, IStateUpdater runner, ICallDataSerializer callDataSerializer, IWhitelistedHashChecker whitelistedHashChecker, IStateRepositoryRoot stateRepository)
        {
            this.logger = loggerFactory.CreateLogger(typeof(StateUpdateExecutor).FullName);
            this.runner = runner;
            this.stateRepository = stateRepository;
            this.whitelistedHashChecker = whitelistedHashChecker;
            this.callDataSerializer = callDataSerializer;
        }

        /// <summary>
        /// Parses data from a transaction and calls a method on a class to calculate a new hash state root.
        /// </summary>
        public IContractExecutionResult Execute(IContractTransactionContext transactionContext)
        {
            Result<ContractTxData> callDataDeserializationResult = this.callDataSerializer.Deserialize(transactionContext.Data);
            ContractTxData callData = callDataDeserializationResult.Value;

            var initialStateRoot = this.stateRepository.Root.ToArray(); // Use ToArray to make a copy

            var paddedIdentifier = new EmbeddedCodeHash(callData.ContractAddress);
            var systemContractCall = new StateUpdateCall(paddedIdentifier.Id, callData.MethodName, callData.MethodParameters, callData.VmVersion);

            // Check if this identifier is currently allowed to update the state.
            if (!this.whitelistedHashChecker.CheckHashWhitelisted(paddedIdentifier.ToBytes()))
            {
                this.logger.LogDebug("Contract is not whitelisted '{0}'.", systemContractCall.Identifier);

                return new StateUpdateExecutionResult(callData.ContractAddress, null);
            }

            // Make some context and invoke the method on the class.
            var context = new StateUpdateContext(this.stateRepository, transactionContext.Transaction, systemContractCall);
            IStateUpdateResult result = this.runner.Execute(context);

            // Only update if there was a change.
            if (!result.NewState.Root.SequenceEqual(initialStateRoot))
            {
                this.stateRepository.SyncToRoot(result.NewState.Root);
            }

            return new StateUpdateExecutionResult(callData.ContractAddress, result.Result);
        }
    }
}
