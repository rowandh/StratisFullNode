using System;
using Moq;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;
using Xunit;

namespace Stratis.Features.SystemContracts.Tests
{
    public class SystemContractExecutorFactoryTests
    {
        [Fact]
        public void Executor_Not_Null()
        {
            var factory = new SystemContractExecutorFactory();

            Assert.NotNull(factory.CreateExecutor(Mock.Of<IStateRepositoryRoot>(), Mock.Of<IContractTransactionContext>()));
        }
    }
}
