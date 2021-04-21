using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Features.SmartContracts.PoW;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Bitcoin.Features.SmartContracts.SystemContracts
{
    public class TestSystemContract : ISystemContract
    {
        // Contract has a dependency
        public TestSystemContract(Network network)
        {
            this.Network = network;
        }

        public Network Network { get; }

        public SystemContractExecutionResult Invoke(SystemContractExecutionContext context)
        {
            // Contract can handle its own parameter serialization
            var messages = context.Data.Select(b => Encoding.UTF8.GetString((byte[])b)).ToList();

            if (messages.Count < 1)
                return new SystemContractExecutionResult();

            var firstMessage = $"{Network.Name}: { messages.First()}";

            // We will need to replace SetStorageValue this with something that doesn't depend on an address
            context.State.SetStorageValue(new uint160(), Encoding.UTF8.GetBytes("FirstMessage"), Encoding.UTF8.GetBytes(firstMessage));

            return new SystemContractExecutionResult();
        }
    }

    /// <summary>
    /// Defines a standard interface for executing a system contract. This is just an idea,
    /// contracts are just standard C# classes and don't actually need to adhere to a specific interface.
    /// </summary>
    public interface ISystemContract
    {
        public SystemContractExecutionResult Invoke(SystemContractExecutionContext context);
    }

    public class SystemContractExecutionContext
    {
        public SystemContractExecutionContext(IStateRepository trackedState, string name, object[] data)
        {
            this.State = trackedState;
            this.Name = name;
            this.Data = data;
        }

        public IStateRepository State { get; }
        public string Name { get; }
        public object[] Data { get; }
    }

    public class SystemContractExecutionResult
    {
        // TODO add things here that we might want to return
    }

    /// <summary>
    /// Controls the lifecycle of the system contracts. Most of the time we can create new instances but this also allows us to use DI or any other means
    /// of instantiating a particular contract. We could even read bytecode from a state database and inflate a type based on that.
    /// </summary>
    public class SystemContractFactory : ISystemContractFactory
    {
        public SystemContractFactory(Network network)
        {
            this.Network = network;
        }

        public Network Network { get; }

        public ISystemContract Create(string name)
        {
            if (name == "test")
            {
                return new TestSystemContract(Network);
            }

            return null; // TODO handle unknown contracts
        }
    }

    /// <summary>
    /// Invokes the system contracts with the necessary state.
    /// </summary>
    public class SystemContractRule : FullValidationConsensusRule
    {
        private readonly IStateRepositoryRoot stateRepositoryRoot;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly ISmartContractActivationProvider smartContractActivationProvider;
        private readonly ISystemContractFactory systemContractFactory;

        public SystemContractRule(IStateRepositoryRoot stateRepositoryRoot, ICallDataSerializer callDataSerializer, ISystemContractFactory systemcontractFactory, ISmartContractActivationProvider smartContractActivationProvider = null)
        {
            this.stateRepositoryRoot = stateRepositoryRoot;
            this.callDataSerializer = callDataSerializer;
            this.smartContractActivationProvider = smartContractActivationProvider;
            this.systemContractFactory = systemcontractFactory;
        }

        public override Task RunAsync(RuleContext context)
        {
            if (this.smartContractActivationProvider?.SkipRule(context) ?? false)
                return Task.CompletedTask;

            BlockHeader prevHeader = context.ValidationContext.ChainedHeaderToValidate.Previous.Header;
            uint256 blockRoot;
            if (!(prevHeader is PosBlockHeader posHeader) || posHeader.HasSmartContractFields)
                blockRoot = ((ISmartContractBlockHeader)prevHeader).HashStateRoot;
            else
                blockRoot = SmartContractBlockDefinition.StateRootEmptyTrie;

            IStateRepositoryRoot state = this.stateRepositoryRoot.GetSnapshotTo(blockRoot.ToBytes());

            // For POC - just use existing smart contract call format. We can change this.
            // TODO transaction ordering is important here eg. if there are two transactions that update
            // a system contract how do we decide in which order they are applied?
            foreach (Transaction transaction in context.ValidationContext.BlockToValidate.Transactions)
            {
                IStateRepository trackedState = state.StartTracking();

                // Skip non-contract txs.
                TxOut smartContractTxOut = transaction.Outputs.FirstOrDefault(txOut => txOut.ScriptPubKey.IsSmartContractExec());

                if (smartContractTxOut == null)
                {
                    continue;
                }
                
                var data = transaction.Outputs.FirstOrDefault(x => x.ScriptPubKey.IsSmartContractExec()).ScriptPubKey.ToBytes();

                CSharpFunctionalExtensions.Result<ContractTxData> contractTxData = this.callDataSerializer.Deserialize(data);
                
                var executionContext = new SystemContractExecutionContext(trackedState, contractTxData.Value.MethodName, contractTxData.Value.MethodParameters);

                // We can change this to different ways of getting contracts
                ISystemContract contract = this.systemContractFactory.Create(executionContext.Name);

                SystemContractExecutionResult result = contract.Invoke(executionContext);

                // If no changes to state were made, nothing will happen
                trackedState.Commit();
            }

            // Commit the whole block
            state.Commit();

            return Task.CompletedTask;
        }
    }
}
