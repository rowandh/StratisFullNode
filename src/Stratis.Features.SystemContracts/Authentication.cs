﻿using Stratis.SCL.Crypto;
using Stratis.SmartContracts;

namespace Stratis.Features.SystemContracts
{
    public class Authentication : SmartContract
    {
        const string primaryGroup = "main";

        public Authentication(ISmartContractState state, byte[] signatories, uint quorum) : base(state)
        {
            this.SetSignatories(primaryGroup, this.Serializer.ToArray<Address>(signatories));
            this.SetQuorum(primaryGroup, quorum);
        }

        public void VerifySignatures(string group, byte[] signatures, string authorizationChallenge)
        {
            string[] sigs = this.Serializer.ToArray<string>(signatures);

            Assert(ECRecover.TryGetVerifiedSignatures(sigs, authorizationChallenge, this.GetSignatories(group), out Address[] verifieds), "Invalid signatures");

            uint quorum = this.GetQuorum(group);

            Assert(verifieds.Length >= quorum, $"Please provide {quorum} valid signatures for '{authorizationChallenge}' from '{group}'.");
        }

        public Address[] GetSignatories(string group)
        {
            Assert(!string.IsNullOrEmpty(group));
            return this.State.GetArray<Address>($"Signatories:{group}");
        }

        private void SetSignatories(string group, Address[] values)
        {
            this.State.SetArray($"Signatories:{group}", values);
        }

        public uint GetQuorum(string group)
        {
            Assert(!string.IsNullOrEmpty(group));
            return this.State.GetUInt32($"Quorum:{group}");
        }

        private void SetQuorum(string group, uint value)
        {
            this.State.SetUInt32($"Quorum:{group}", value);
        }

        private uint GetGroupNonce(string group)
        {
            return this.State.GetUInt32($"GroupNonce:{group}");
        }

        private void SetGroupNonce(string group, uint value)
        {
            this.State.SetUInt32($"GroupNonce:{group}", value);
        }

        public void AddSignatory(byte[] signatures, string group, Address address, uint newSize, uint newQuorum)
        {
            Assert(!string.IsNullOrEmpty(group));
            Assert(newSize >= newQuorum, "The number of signatories can't be less than the quorum.");

            Address[] signatories = this.GetSignatories(group);
            foreach (Address signatory in signatories)
                Assert(signatory != address, "The signatory already exists.");

            Assert((signatories.Length + 1) == newSize, "The expected size is incorrect.");

            // The nonce is used to prevent replay attacks.
            uint nonce = this.GetGroupNonce(group);

            // Validate or provide a unique challenge to the signatories that depends on the exact action being performed.
            // If the signatures are missing or fail validation contract execution will stop here.
            this.VerifySignatures(primaryGroup, signatures, $"{nameof(AddSignatory)}(Nonce:{nonce},Group:{group},Address:{address},NewSize:{newSize},NewQuorum:{newQuorum})");

            System.Array.Resize(ref signatories, signatories.Length + 1);
            signatories[signatories.Length - 1] = address;

            this.SetSignatories(group, signatories);
            this.SetQuorum(group, newQuorum);
            this.SetGroupNonce(group, nonce + 1);
        }

        public void RemoveSignatory(byte[] signatures, string group, Address address, uint newSize, uint newQuorum)
        {
            Assert(!string.IsNullOrEmpty(group));
            Assert(newSize >= newQuorum, "The number of signatories can't be less than the quorum.");

            Address[] prevSignatories = this.GetSignatories(group);
            Address[] signatories = new Address[prevSignatories.Length - 1];

            int i = 0;
            foreach (Address item in prevSignatories)
            {
                if (item == address)
                {
                    continue;
                }

                Assert(signatories.Length != i, "The signatory does not exist.");

                signatories[i++] = item;
            }

            Assert(newSize == signatories.Length, "The expected size is incorrect.");

            // The nonce is used to prevent replay attacks.
            uint nonce = this.GetGroupNonce(group);

            // Validate or provide a unique challenge to the signatories that depends on the exact action being performed.
            // If the signatures are missing or fail validation contract execution will stop here.
            this.VerifySignatures(primaryGroup, signatures, $"{nameof(RemoveSignatory)}(Nonce:{nonce},Group:{group},Address:{address},NewSize:{newSize},NewQuorum:{newQuorum})");

            this.SetSignatories(group, signatories);
            this.SetQuorum(group, newQuorum);
            this.SetGroupNonce(group, nonce + 1);
        }
    }
}
