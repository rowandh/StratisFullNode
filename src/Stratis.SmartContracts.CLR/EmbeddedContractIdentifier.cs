﻿using System;
using System.Linq;
using NBitcoin;

namespace Stratis.SmartContracts.CLR
{
    public struct EmbeddedContractIdentifier
    {
        private static byte[] embeddedContractSignature = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        private uint160 value;

        public EmbeddedContractIdentifier(uint160 id)
        {
            this.value = id;
        }

        public EmbeddedContractIdentifier(ulong contractTypeId, uint version)
        {
            this.value = new uint160(embeddedContractSignature.Concat(BitConverter.GetBytes(contractTypeId)).Concat(BitConverter.GetBytes(version)).ToArray());
        }

        public ulong ContractTypeId { get => BitConverter.ToUInt64(this.value.ToBytes(), 8); }

        public uint Version { get => BitConverter.ToUInt32(this.value.ToBytes(), 16); }

        public static implicit operator uint160(EmbeddedContractIdentifier embeddedContractIdentifier)
        {
            return embeddedContractIdentifier.value;
        }

        public static bool IsEmbedded(uint160 id)
        {
            return id.GetLow64() == BitConverter.ToUInt64(embeddedContractSignature);
        }

        public uint160 Data => this.value;
    }

}
