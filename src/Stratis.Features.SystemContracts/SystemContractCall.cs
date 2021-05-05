﻿using Stratis.SmartContracts.CLR;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Defines an on-chain call to a system contract.
    /// </summary>
    public class SystemContractCall
    {
        public SystemContractCall(EmbeddedContractIdentifier identifier, string methodName, object[] parameters, int version = 1)
        {
            this.Identifier = identifier;
            this.MethodName = methodName;
            this.Parameters = parameters;
            this.Version = version;
        }

        public EmbeddedContractIdentifier Identifier { get; }
        public string MethodName { get; }
        public object[] Parameters { get; }
        public int Version { get; }
    }
}
