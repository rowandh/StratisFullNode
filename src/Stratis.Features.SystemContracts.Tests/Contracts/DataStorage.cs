using System;
using System.Linq;
using System.Text;
using CSharpFunctionalExtensions;
using NBitcoin;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts.Tests.Contracts
{
    /// <summary>
    /// Sample contract that uses auth and stores data in the state.
    /// </summary>
    public class DataStorage
    {
        public DataStorage(IStateRepositoryRoot state, Network network, AuthorizationStateCheck auth)
        {
            this.State = state;
            this.Network = network;
            this.Auth = auth;

            if (!this.Initialized)
            {
                this.State.SetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes("Network"), Encoding.UTF8.GetBytes(network.Name));
                this.Initialized = true;
            }
        }

        public bool Initialized
        {
            get
            {
                var data = this.State.GetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes("Initialized"));
                return data == null ? false : BitConverter.ToBoolean(data);
            }

            set
            {
                this.State.SetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes("Initialized"), BitConverter.GetBytes(value));
            }
        }

        /// <summary>
        /// Example of a unique identifier, which we need to fit in a uint160 somehow. We can change this.
        /// </summary>
        public static EmbeddedContractIdentifier Identifier => new EmbeddedContractIdentifier(new uint160(SCL.Crypto.SHA3.Keccak256(Encoding.UTF8.GetBytes(nameof(DataStorage))).Take(20).ToArray()));

        public IStateRepositoryRoot State { get; }

        public Network Network { get; }

        public AuthorizationStateCheck Auth { get; }

        public bool AddData(string[] signatories, string key, string value)
        {
            if (!this.Auth.IsAuthorised(signatories))
                return false;

            this.State.SetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));

            return true;
        }

        public bool AddData(string[] signatories, string key, string value, bool appendPrefix = true)
        {
            if (!this.Auth.IsAuthorised(signatories))
                return false;

            value = appendPrefix ? "prefix-" + value : value;

            this.State.SetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));

            return true;
        }

        public void PersistComplexType(Transaction tx)
        {
            this.State.SetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes("Tx"), tx.ToBytes());
        }

        public void PersistComplexTypeButDoSerializationInTheParentClassInsteadOfTheDispatcher(string txRawHex)
        {
            var tx = Transaction.Parse(txRawHex, RawFormat.Satoshi);

            this.State.SetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes("Tx"), tx.ToBytes());
        }

        public class Dispatcher : IDispatcher<DataStorage>
        {
            private readonly Network network;
            private readonly IDispatcher<AuthorizationStateCheck> authContract;

            public Dispatcher(Network network, IDispatcher<AuthorizationStateCheck> authContract)
            {
                this.network = network;
                this.authContract = authContract;
            }

            public EmbeddedContractIdentifier Identifier => DataStorage.Identifier;

            public DataStorage GetInstance(IStateUpdateContext context)
            {
                return new DataStorage(context.State, this.network, this.authContract.GetInstance(context));
            }

            /// <summary>
            /// Instantiates the type, finds the method to call and dispatches the call.
            /// </summary>
            /// <param name="context"></param>
            /// <returns>A result indicating whether or not the execution was successful.</returns>
            public Result<object> Dispatch(IStateUpdateContext context)
            {
                DataStorage instance = GetInstance(context);

                switch (context.CallData.MethodName)
                {
                    case nameof(DataStorage.AddData):
                        if (context.CallData.Parameters.Length == 3)
                        {
                            var result = instance.AddData(context.CallData.Parameters[0] as string[], context.CallData.Parameters[1] as string, context.CallData.Parameters[2] as string);
                            return Result.Ok<object>(result);
                        }

                        if (context.CallData.Parameters.Length == 4)
                        {
                            var result = instance.AddData(context.CallData.Parameters[0] as string[], context.CallData.Parameters[1] as string, context.CallData.Parameters[2] as string, (bool)context.CallData.Parameters[3]);
                            return Result.Ok<object>(result);
                        }

                        return Result.Fail<object>($"Method {context.CallData.MethodName} overload with {context.CallData.Parameters.Length} params does not exist on type {nameof(DataStorage)} v{context.CallData.Version}");

                    case nameof(DataStorage.PersistComplexType):
                        var txRawHex = context.CallData.Parameters[0] as string;

                        var tx = Transaction.Parse(txRawHex, RawFormat.Satoshi);

                        instance.PersistComplexType(tx);

                        return Result.Ok<object>(DispatchResult.Void);

                    case nameof(DataStorage.PersistComplexTypeButDoSerializationInTheParentClassInsteadOfTheDispatcher):
                        instance.PersistComplexTypeButDoSerializationInTheParentClassInsteadOfTheDispatcher(context.CallData.Parameters[0] as string);
                        return Result.Ok<object>(DispatchResult.Void);

                    default:
                        return Result.Fail<object>($"Method {context.CallData.MethodName} does not exist on type {nameof(DataStorage)} v{context.CallData.Version}");
                }
            }
        }
    }
}
