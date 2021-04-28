﻿using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Consensus.Interfaces;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.SmartContracts.Caching;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Features.SmartContracts.PoW;
using Stratis.Bitcoin.Features.SmartContracts.Rules;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Mining;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.SmartContracts;
using Stratis.Features.SystemContracts;
using Stratis.Features.SystemContracts.Contracts;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.CLR.Decompilation;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.CLR.Local;
using Stratis.SmartContracts.CLR.ResultProcessors;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.CLR.Validation;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.Util;

namespace Stratis.Bitcoin.Features.SmartContracts
{
    public class SystemContractFeature : FullNodeFeature
    {
        private readonly IConsensusManager consensusManager;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IStateRepositoryRoot stateRoot;

        public SystemContractFeature(IConsensusManager consensusLoop, ILoggerFactory loggerFactory, Network network, IStateRepositoryRoot stateRoot, ISmartContractActivationProvider smartContractPosActivationProvider = null)
        {
            this.consensusManager = consensusLoop;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.stateRoot = stateRoot;

            if (network.Consensus.ConsensusFactory is SmartContractPoSConsensusFactory)
            {
                Guard.NotNull(smartContractPosActivationProvider, nameof(smartContractPosActivationProvider));
            }
        }

        public override Task InitializeAsync()
        {
            Guard.Assert(this.network.Consensus.ConsensusFactory is SmartContractPowConsensusFactory
                         || this.network.Consensus.ConsensusFactory is SmartContractPoSConsensusFactory
                         || this.network.Consensus.ConsensusFactory is SmartContractPoAConsensusFactory
                         || this.network.Consensus.ConsensusFactory is SmartContractCollateralPoAConsensusFactory);

            this.stateRoot.SyncToRoot(((ISmartContractBlockHeader)this.consensusManager.Tip.Header).HashStateRoot.ToBytes());

            this.logger.LogInformation("System Contract Feature Injected.");
            return Task.CompletedTask;
        }
    }

    public class SystemContractOptions
    {
        public SystemContractOptions(IServiceCollection services, Network network)
        {
            this.Services = services;
            this.Network = network;
        }

        public IServiceCollection Services { get; }
        public Network Network { get; }
    }

    public static partial class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Adds the smart contract feature to the node.
        /// </summary>
        public static IFullNodeBuilder AddSystemContracts(this IFullNodeBuilder fullNodeBuilder, Action<SystemContractOptions> options = null, Action<SystemContractOptions> preOptions = null)
        {
            LoggingConfiguration.RegisterFeatureNamespace<SmartContractFeature>("systemcontracts");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<SystemContractFeature>()
                    .FeatureServices(services =>
                    {
                        // Before setting up, invoke any additional options.
                        preOptions?.Invoke(new SystemContractOptions(services, fullNodeBuilder.Network));

                        // STATE ----------------------------------------------------------------------------
                        services.AddSingleton<DBreezeContractStateStore>();
                        services.AddSingleton<NoDeleteContractStateSource>();
                        services.AddSingleton<IStateRepositoryRoot, StateRepositoryRoot>();
                        services.AddSingleton<ISmartContractActivationProvider, SmartContractPosActivationProvider>();

                        // CONSENSUS ------------------------------------------------------------------------

                        // TODO adjust the mempool validator and default transaction policy
                        services.Replace(ServiceDescriptor.Singleton<IMempoolValidator, SmartContractMempoolValidator>());
                        services.AddSingleton<StandardTransactionPolicy, SmartContractTransactionPolicy>();

                        // CONTRACT EXECUTION ---------------------------------------------------------------
                        services.AddSingleton<IInternalExecutorFactory, InternalExecutorFactory>();
                        services.AddSingleton<IContractAssemblyCache, ContractAssemblyCache>();
                        services.AddSingleton<IVirtualMachine, ReflectionVirtualMachine>();
                        services.AddSingleton<IAddressGenerator, AddressGenerator>();
                        services.AddSingleton<ILoader, ContractAssemblyLoader>();
                        services.AddSingleton<IContractModuleDefinitionReader, ContractModuleDefinitionReader>();
                        services.AddSingleton<IStateFactory, StateFactory>();
                        services.AddSingleton<SmartContractTransactionPolicy>();
                        services.AddSingleton<IStateProcessor, StateProcessor>();
                        services.AddSingleton<ISmartContractStateFactory, SmartContractStateFactory>();
                        services.AddSingleton<ILocalExecutor, LocalExecutor>();
                        services.AddSingleton<IBlockExecutionResultCache, BlockExecutionResultCache>();
                        services.AddSingleton<IContractPrimitiveSerializer, ContractPrimitiveSerializer>();

                        // RECEIPTS -------------------------------------------------------------------------
                        services.AddSingleton<IReceiptRepository, PersistentReceiptRepository>();

                        // UTILS ----------------------------------------------------------------------------
                        services.AddSingleton<ISenderRetriever, SenderRetriever>();

                        services.AddSingleton<IMethodParameterSerializer, MethodParameterByteSerializer>();
                        services.AddSingleton<IMethodParameterStringSerializer, MethodParameterStringSerializer>();
                        services.AddSingleton<ICallDataSerializer, CallDataSerializer>();

                        // MINING ---------------------------------------------------------------------------
                        if (fullNodeBuilder.Network.Consensus.ConsensusFactory is SmartContractPoSConsensusFactory)
                        {
                            services.AddSingleton<IBlockBufferGenerator, BlockBufferGenerator>();
                            services.AddSingleton<BlockDefinition, SmartContractPosBlockDefinition>();
                            services.AddSingleton<BlockDefinition, PosBlockDefinition>();
                        }

                        // Registers the ScriptAddressReader concrete type and replaces the IScriptAddressReader implementation
                        // with SmartContractScriptAddressReader, which depends on the ScriptAddressReader concrete type.
                        services.AddSingleton<ScriptAddressReader>();
                        services.Replace(new ServiceDescriptor(typeof(IScriptAddressReader), typeof(SmartContractScriptAddressReader), ServiceLifetime.Singleton));

                        services.AddSingleton<IDispatcherRegistry, DispatcherRegistry>();
                        services.AddSingleton<ISystemContractRunner, SystemContractRunner>();
                        services.AddSingleton<ISmartContractCoinViewRuleLogic, SystemContractCoinViewRuleLogic>();
                        services.AddSingleton<ISystemContractContainer, SystemContractContainer>();
                        services.AddSingleton<IWhitelistedHashChecker, PoSWhitelistedHashChecker>();

                        // System contracts. TBD do we need to register both types here?
                        services.AddSingleton<IDispatcher, DataStorageContract.Dispatcher>();
                        services.AddSingleton<IDispatcher<DataStorageContract>, DataStorageContract.Dispatcher>();
                        services.AddSingleton<IDispatcher, AuthContract.Dispatcher>();
                        services.AddSingleton<IDispatcher<AuthContract>, AuthContract.Dispatcher>();

                        // After setting up, invoke any additional options which can replace services as required.
                        options?.Invoke(new SystemContractOptions(services, fullNodeBuilder.Network));
                    });
            });

            return fullNodeBuilder;
        }

        /// <summary>
        /// This node will be configured with the reflection contract executor.
        /// <para>
        /// Should we require another executor, we will need to create a separate daemon and network.
        /// </para>
        /// </summary>
        public static SystemContractOptions UseReflectionExecutor(this SystemContractOptions options)
        {
            IServiceCollection services = options.Services;

            // Validator
            services.AddSingleton<ISmartContractValidator, SmartContractValidator>();

            // Executor et al.
            services.AddSingleton<IContractRefundProcessor, ContractRefundProcessor>();
            services.AddSingleton<IContractTransferProcessor, ContractTransferProcessor>();
            services.AddSingleton<IKeyEncodingStrategy, BasicKeyEncodingStrategy>();
            services.AddSingleton<IContractExecutorFactory, ReflectionExecutorFactory>();
            services.AddSingleton<IMethodParameterSerializer, MethodParameterByteSerializer>();
            services.AddSingleton<IContractPrimitiveSerializer, ContractPrimitiveSerializer>();
            services.AddSingleton<ISerializer, Serializer>();
            services.AddSingleton<ISmartContractCoinViewRuleLogic, SmartContractCoinViewRuleLogic>();

            // Controllers + utils
            services.AddSingleton<CSharpContractDecompiler>();

            return options;
        }
    }
}
