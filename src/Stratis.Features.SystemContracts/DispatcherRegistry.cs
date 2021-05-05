using System.Collections.Generic;
using System.Linq;
using Stratis.SmartContracts.CLR;

namespace Stratis.Features.SystemContracts
{
    public interface IDispatcherRegistry
    {
        IDispatcher GetDispatcher(EmbeddedContractIdentifier identifier);
        bool HasDispatcher(EmbeddedContractIdentifier identifier);
    }

    /// <summary>
    /// Keeps track of the system contract dispatchers required for dynamic invocation.
    /// </summary>
    public class DispatcherRegistry : IDispatcherRegistry
    {
        private readonly Dictionary<EmbeddedContractIdentifier, IDispatcher> dispatchers;

        // DI will inject all registered IDispatcher instances as an IEnumerable.
        public DispatcherRegistry(IEnumerable<IDispatcher> dispatchers)
        {
            this.dispatchers = dispatchers.ToDictionary(k => k.Identifier, v => v);
        }

        public bool HasDispatcher(EmbeddedContractIdentifier identifier)
        {
            return this.dispatchers.ContainsKey(identifier);
        }

        public IDispatcher GetDispatcher(EmbeddedContractIdentifier identifier)
        {
            return this.dispatchers[identifier];
        }
    }
}
