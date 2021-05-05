using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stratis.Bitcoin.Networks;
using Stratis.Features.SystemContracts.Tests.Contracts;
using Stratis.Patricia;
using Stratis.SmartContracts.Core.State;
using Xunit;

namespace Stratis.Features.SystemContracts.Tests
{
    public class EndToEndTests
    {
        [Fact]
        public void Complete_Execution_Success()
        {
            var network = new StraxMain();
            var authContract = new AuthorizationStateCheck.Dispatcher(network.EmbeddedContractContainer);
            var dataStorageContract = new DataStorage.Dispatcher(network, authContract);

            var dispatchers = new List<IDispatcher>
            {
                authContract,
                dataStorageContract
            };

            var dispatcherRegistry = new DispatcherRegistry(dispatchers);
            var runner = new StateUpdater(dispatcherRegistry);

            var state = new StateRepositoryRoot(new MemoryDictionarySource());

            var initialRoot = state.Root.ToArray();

            var key = "Key";
            var value = "Value";
            var @params = new object[]
            {
                new string[] { "secret" },
                key,
                value
            };

            var callData = new StateUpdateCall(DataStorage.Identifier, nameof(DataStorage.AddData), @params, 1);

            var context = new StateUpdateContext(state, null /* This isn't used anywhere at the moment */, callData);

            IStateUpdateResult result = runner.Execute(context);

            Assert.Equal(true, result.Result);

            // State has changed
            Assert.False(initialRoot.SequenceEqual(state.Root));

            // Query the state directly.
            var storedData = state.GetStorageValue(DataStorage.Identifier.Data, Encoding.UTF8.GetBytes(key));
            Assert.Equal(value, Encoding.UTF8.GetString(storedData));
        }

        [Fact]
        public void Complete_Execution_Fails()
        {
            var network = new StraxMain();
            var authContract = new AuthorizationStateCheck.Dispatcher(network.EmbeddedContractContainer);
            var dataStorageContract = new DataStorage.Dispatcher(network, authContract);

            var dispatchers = new List<IDispatcher>
            {
                authContract,
                dataStorageContract
            };

            var dispatcherRegistry = new DispatcherRegistry(dispatchers);
            var runner = new StateUpdater(dispatcherRegistry);

            var state = new StateRepositoryRoot(new MemoryDictionarySource());

            var initialRoot = state.Root.ToArray();

            var @params = new object[] {};

            var callData = new StateUpdateCall(DataStorage.Identifier, "MethodThatDoesntExist", @params, 1);

            var context = new StateUpdateContext(state, null /* This isn't used anywhere at the moment */, callData);

            IStateUpdateResult result = runner.Execute(context);

            Assert.Null(result.Result);

            // State has not changed
            Assert.True(initialRoot.SequenceEqual(state.Root));
        }
    }
}
