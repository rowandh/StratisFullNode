using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.PoW;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.Core.Util;
using Stratis.SmartContracts.Tests.Common.MockChain;
using Xunit;

namespace Stratis.SmartContracts.IntegrationTests
{
    public abstract class ContractBehaviourTests<T> : IClassFixture<T> where T : class, IMockChainFixture
    {
        private readonly IMockChain mockChain;
        private readonly MockChainNode node1;
        private readonly MockChainNode node2;
        private readonly ChainIndexer chainIndexer1;
        private readonly IAddressGenerator addressGenerator;
        private readonly ISenderRetriever senderRetriever;

        protected ContractBehaviourTests(T fixture)
        {
            this.mockChain = fixture.Chain;
            this.node1 = this.mockChain.Nodes[0];
            this.node2 = this.mockChain.Nodes[1];
            this.chainIndexer1 = this.node1.CoreNode.FullNode.NodeService<ChainIndexer>();
            this.addressGenerator = new AddressGenerator();
            this.senderRetriever = new SenderRetriever();
        }

        [Fact]
        public void Demonstrate_HashStateRoot_Changes()
        {
            // Demonstrates how the blockheader's hashstateroot changes with contract executions

            // Ensure fixture is funded.
            this.mockChain.MineBlocks(1);

            // HashStateRoot begins with the StateRootEmptyTrie
            uint256 lastHashStateRoot = ((ISmartContractBlockHeader)this.node1.GetLastBlock().Header).HashStateRoot;
            Assert.Equal(SmartContractBlockDefinition.StateRootEmptyTrie, lastHashStateRoot);

            // Deploy a contract
            ContractCompilationResult compilationResult = ContractCompiler.CompileFile("SmartContracts/StorageDemo.cs");
            Assert.True(compilationResult.Success);
            BuildCreateContractTransactionResponse preResponse = this.node1.SendCreateContractTransaction(compilationResult.Compilation, 0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);
            Assert.NotNull(this.node1.GetCode(preResponse.NewContractAddress));
                       
            var height = this.chainIndexer1.Tip.Height;

            uint256 thisHashStateRoot = ((ISmartContractBlockHeader)this.node1.GetLastBlock().Header).HashStateRoot;

            // After contract deployment the hash state root has changed
            Assert.NotEqual(lastHashStateRoot, thisHashStateRoot);            
            lastHashStateRoot = thisHashStateRoot;

            BuildCallContractTransactionResponse response = this.node1.SendCallContractTransaction(
                nameof(StorageDemo.Increment),
                preResponse.NewContractAddress,
                0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            // Expect hash state root has changed because we did some contract stuff.
            thisHashStateRoot = ((ISmartContractBlockHeader)this.node1.GetLastBlock().Header).HashStateRoot;
            Assert.NotEqual(lastHashStateRoot, thisHashStateRoot);
            Assert.NotEqual(height, this.chainIndexer1.Tip.Height);
            height = this.chainIndexer1.Tip.Height;
            lastHashStateRoot = thisHashStateRoot;

            // Mine another block with no contract transactions
            this.mockChain.MineBlocks(1);

            // If no contract transactions expect hash state root has NOT changed
            thisHashStateRoot = ((ISmartContractBlockHeader)this.node1.GetLastBlock().Header).HashStateRoot;
            Assert.Equal(lastHashStateRoot, thisHashStateRoot);
            Assert.NotEqual(height, this.chainIndexer1.Tip.Height);
            height = this.chainIndexer1.Tip.Height;

            // Invoke something that changes the state trie again
             response = this.node1.SendCallContractTransaction(
                nameof(StorageDemo.Increment),
                preResponse.NewContractAddress,
                0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            thisHashStateRoot = ((ISmartContractBlockHeader)this.node1.GetLastBlock().Header).HashStateRoot;

            // Expect hash state root has changed again
            Assert.NotEqual(lastHashStateRoot, thisHashStateRoot);
            Assert.NotEqual(height, this.chainIndexer1.Tip.Height);
        }
    }

    public class PoAContractBehaviourTests : ContractBehaviourTests<PoAMockChainFixture>
    {
        public PoAContractBehaviourTests(PoAMockChainFixture fixture) : base(fixture)
        {
        }
    }

    public class PoWContractBehaviourTests : ContractBehaviourTests<PoWMockChainFixture>
    {
        public PoWContractBehaviourTests(PoWMockChainFixture fixture) : base(fixture)
        {
        }
    }
}
