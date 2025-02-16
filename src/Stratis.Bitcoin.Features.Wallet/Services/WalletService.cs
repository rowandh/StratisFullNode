using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.Wallet.Broadcasting;
using Stratis.Bitcoin.Features.Wallet.Controllers;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Wallet.Services
{
    public class WalletService : IWalletService
    {
        protected readonly IWalletManager walletManager;
        private readonly IWalletTransactionHandler walletTransactionHandler;
        private readonly IWalletSyncManager walletSyncManager;
        private readonly IConnectionManager connectionManager;
        private readonly IConsensusManager consensusManager;
        protected readonly Network network;
        private readonly ChainIndexer chainIndexer;
        private readonly IBroadcasterManager broadcasterManager;
        private readonly IDateTimeProvider dateTimeProvider;
        private readonly CoinType coinType;
        private readonly ILogger logger;
        private readonly IUtxoIndexer utxoIndexer;
        private readonly IWalletFeePolicy walletFeePolicy;
        private readonly NodeSettings nodeSettings;

        public WalletService(ILoggerFactory loggerFactory,
            IWalletManager walletManager,
            IConsensusManager consensusManager,
            IWalletTransactionHandler walletTransactionHandler,
            IWalletSyncManager walletSyncManager,
            IConnectionManager connectionManager,
            Network network,
            ChainIndexer chainIndexer,
            IBroadcasterManager broadcasterManager,
            IDateTimeProvider dateTimeProvider,
            IUtxoIndexer utxoIndexer,
            IWalletFeePolicy walletFeePolicy,
            NodeSettings nodeSettings)
        {
            this.walletManager = walletManager;
            this.consensusManager = consensusManager;
            this.walletTransactionHandler = walletTransactionHandler;
            this.walletSyncManager = walletSyncManager;
            this.connectionManager = connectionManager;
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.broadcasterManager = broadcasterManager;
            this.dateTimeProvider = dateTimeProvider;
            this.coinType = (CoinType)network.Consensus.CoinType;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.utxoIndexer = utxoIndexer;
            this.walletFeePolicy = walletFeePolicy;
            this.nodeSettings = nodeSettings;
        }

        public async Task<IEnumerable<string>> GetWalletNames(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() => this.walletManager.GetWalletsNames(), cancellationToken);
        }

        public async Task<string> CreateWallet(WalletCreationRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                try
                {
                    Mnemonic requestMnemonic =
                        string.IsNullOrEmpty(request.Mnemonic) ? null : new Mnemonic(request.Mnemonic);

                    (_, Mnemonic mnemonic) = this.walletManager.CreateWallet(request.Password, request.Name,
                        request.Passphrase, mnemonic: requestMnemonic);

                    return mnemonic.ToString();
                }
                catch (WalletException e)
                {
                    // indicates that this wallet already exists
                    this.logger.LogError("Exception occurred: {0}", e.ToString());
                    throw new FeatureException(HttpStatusCode.Conflict, e.Message, e.ToString());
                }
                catch (NotSupportedException e)
                {
                    this.logger.LogError("Exception occurred: {0}", e.ToString());
                    throw new FeatureException(HttpStatusCode.BadRequest,
                        "There was a problem creating a wallet.", e.ToString());
                }
            }, cancellationToken);
        }

        public async Task<AddressBalanceModel> GetReceivedByAddress(string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                AddressBalance balanceResult = this.walletManager.GetAddressBalance(address);
                return new AddressBalanceModel
                {
                    CoinType = this.coinType,
                    Address = balanceResult.Address,
                    AmountConfirmed = balanceResult.AmountConfirmed,
                    AmountUnconfirmed = balanceResult.AmountUnconfirmed,
                    SpendableAmount = balanceResult.SpendableAmount
                };
            }, cancellationToken);
        }

        public async Task<WalletGeneralInfoModel> GetWalletGeneralInfo(string walletName,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                Wallet wallet = this.walletManager.GetWallet(walletName);
                return new WalletGeneralInfoModel
                {
                    WalletName = wallet.Name,
                    Network = wallet.Network.Name,
                    CreationTime = wallet.CreationTime,
                    LastBlockSyncedHeight = wallet.AccountsRoot.Single().LastBlockSyncedHeight,
                    ConnectedNodes = this.connectionManager.ConnectedPeers.Count(),
                    ChainTip = this.consensusManager.HeaderTip,
                    IsChainSynced = this.chainIndexer.IsDownloaded(),
                    IsDecrypted = true
                };
            }, cancellationToken);
        }

        public async Task<WalletBalanceModel> GetBalance(
            string walletName, string accountName, bool includeBalanceByAddress = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                var model = new WalletBalanceModel();

                IEnumerable<AccountBalance> balances = this.walletManager.GetBalances(walletName, accountName).ToList();

                if (accountName != null && !balances.Any())
                    throw new Exception($"No account with the name '{accountName}' could be found.");

                foreach (AccountBalance balance in balances)
                {
                    HdAccount account = balance.Account;
                    model.AccountsBalances.Add(new AccountBalanceModel
                    {
                        CoinType = this.coinType,
                        Name = account.Name,
                        HdPath = account.HdPath,
                        AmountConfirmed = balance.AmountConfirmed,
                        AmountUnconfirmed = balance.AmountUnconfirmed,
                        SpendableAmount = balance.SpendableAmount,
                        Addresses = includeBalanceByAddress
                            ? account.GetCombinedAddresses().Select(address =>
                            {
                                (Money confirmedAmount, Money unConfirmedAmount) = address.GetBalances(account.IsNormalAccount());
                                return new AddressModel
                                {
                                    Address = address.Address,
                                    IsUsed = address.Transactions.Any(),
                                    IsChange = address.IsChangeAddress(),
                                    AmountConfirmed = confirmedAmount,
                                    AmountUnconfirmed = unConfirmedAmount
                                };
                            })
                            : null
                    });
                }

                return model;
            }, cancellationToken);
        }

        public WalletHistoryModel GetHistory(WalletHistoryRequest request)
        {
            IEnumerable<AccountHistory> accountsHistory;

            if (request.Skip.HasValue && request.Take.HasValue)
                accountsHistory = this.walletManager.GetHistory(request.WalletName, request.AccountName, request.SearchQuery, request.Take.Value, request.Skip.Value, accountAddress: request.Address);
            else
                accountsHistory = this.walletManager.GetHistory(request.WalletName, request.AccountName, request.SearchQuery, accountAddress: request.Address);

            var model = new WalletHistoryModel();

            foreach (AccountHistory accountHistory in accountsHistory)
            {
                var accountHistoryModel = new AccountHistoryModel
                {
                    TransactionsHistory = accountHistory.History.Select(h => new TransactionItemModel(h)).ToList(),
                    Name = accountHistory.Account.Name,
                    CoinType = this.coinType,
                    HdPath = accountHistory.Account.HdPath
                };

                model.AccountsHistoryModel.Add(accountHistoryModel);
            }

            return model;
        }

        public async Task<WalletStatsModel> GetWalletStats(WalletStatsRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                var model = new WalletStatsModel
                {
                    WalletName = request.WalletName
                };

                IEnumerable<UnspentOutputReference> spendableTransactions =
                    this.walletManager.GetSpendableTransactionsInAccount(
                            new WalletAccountReference(request.WalletName, request.AccountName),
                            request.MinConfirmations)
                        .ToList();

                model.TotalUtxoCount = spendableTransactions.Count();
                model.UniqueTransactionCount =
                    spendableTransactions.GroupBy(s => s.Transaction.Id).Select(s => s.Key).Count();
                model.UniqueBlockCount = spendableTransactions.GroupBy(s => s.Transaction.BlockHeight)
                    .Select(s => s.Key).Count();
                model.FinalizedTransactions =
                    spendableTransactions.Count(s => s.Confirmations >= this.network.Consensus.MaxReorgLength);

                if (!request.Verbose)
                {
                    return model;
                }

                model.UtxoAmounts = spendableTransactions
                    .GroupBy(s => s.Transaction.Amount)
                    .OrderByDescending(sg => sg.Count())
                    .Select(sg => new UtxoAmountModel
                    { Amount = sg.Key.ToDecimal(MoneyUnit.BTC), Count = sg.Count() })
                    .ToList();

                // This is number of UTXO originating from the same transaction
                // WalletInputsPerTransaction = 2000 and Count = 1; would be the result of one split coin operation into 2000 UTXOs
                model.UtxoPerTransaction = spendableTransactions
                    .GroupBy(s => s.Transaction.Id)
                    .GroupBy(sg => sg.Count())
                    .OrderByDescending(sgg => sgg.Count())
                    .Select(utxo => new UtxoPerTransactionModel
                    { WalletInputsPerTransaction = utxo.Key, Count = utxo.Count() })
                    .ToList();

                model.UtxoPerBlock = spendableTransactions
                    .GroupBy(s => s.Transaction.BlockHeight)
                    .GroupBy(sg => sg.Count())
                    .OrderByDescending(sgg => sgg.Count())
                    .Select(utxo => new UtxoPerBlockModel { WalletInputsPerBlock = utxo.Key, Count = utxo.Count() })
                    .ToList();

                return model;
            }, cancellationToken);
        }

        public async Task<WalletSendTransactionModel> SplitCoins(SplitCoinsRequest request,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var walletReference = new WalletAccountReference(request.WalletName, request.AccountName);
                HdAddress address = this.walletManager.GetUnusedAddress(walletReference);

                Money totalAmount = request.TotalAmountToSplit;
                Money singleUtxoAmount = totalAmount / request.UtxosCount;

                var recipients = new List<Recipient>(request.UtxosCount);
                for (int i = 0; i < request.UtxosCount; i++)
                    recipients.Add(new Recipient { ScriptPubKey = address.ScriptPubKey, Amount = singleUtxoAmount });

                var context = new TransactionBuildContext(this.network)
                {
                    AccountReference = walletReference,
                    MinConfirmations = 1,
                    Shuffle = true,
                    WalletPassword = request.WalletPassword,
                    Recipients = recipients,
                    Time = (uint)this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp()
                };

                Transaction transactionResult = this.walletTransactionHandler.BuildTransaction(context);

                return this.SendTransaction(new SendTransactionRequest(transactionResult.ToHex()),
                    CancellationToken.None);
            }, cancellationToken);
        }

        public async Task<WalletSendTransactionModel> SendTransaction(SendTransactionRequest request, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                if (this.nodeSettings.DevMode == null && !this.connectionManager.ConnectedPeers.Any())
                {
                    this.logger.LogTrace("(-)[NO_CONNECTED_PEERS]");

                    throw new FeatureException(HttpStatusCode.Forbidden, "Can't send transaction: sending transaction requires at least one connection.", string.Empty);
                }

                Transaction transaction = this.network.CreateTransaction(request.Hex);

                var model = new WalletSendTransactionModel
                {
                    TransactionId = transaction.GetHash(),
                    Outputs = new List<TransactionOutputModel>()
                };

                foreach (TxOut output in transaction.Outputs)
                {
                    bool isUnspendable = output.ScriptPubKey.IsUnspendable;
                    model.Outputs.Add(new TransactionOutputModel
                    {
                        Address = isUnspendable
                            ? null
                            : output.ScriptPubKey.GetDestinationAddress(this.network)?.ToString(),
                        Amount = output.Value,
                        OpReturnData = isUnspendable
                            ? Encoding.UTF8.GetString(output.ScriptPubKey.ToOps().Last().PushData)
                            : null
                    });
                }

                this.broadcasterManager.BroadcastTransactionAsync(transaction).GetAwaiter().GetResult();

                TransactionBroadcastEntry transactionBroadCastEntry =
                    this.broadcasterManager.GetTransaction(transaction.GetHash());

                if (transactionBroadCastEntry.TransactionBroadcastState == TransactionBroadcastState.CantBroadcast)
                {
                    this.logger.LogError("Exception occurred: {0}", transactionBroadCastEntry.ErrorMessage);

                    throw new FeatureException(HttpStatusCode.BadRequest,
                        transactionBroadCastEntry.ErrorMessage, "Transaction Exception");
                }

                return model;
            }, cancellationToken);
        }

        public async Task<IEnumerable<RemovedTransactionModel>> RemoveTransactions(RemoveTransactionsModel request,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                HashSet<(uint256 transactionId, DateTimeOffset creationTime)> result;

                if (request.DeleteAll)
                {
                    result = this.walletManager.RemoveAllTransactions(request.WalletName);
                }
                else if (request.FromDate != default(DateTime))
                {
                    result = this.walletManager.RemoveTransactionsFromDate(request.WalletName, request.FromDate);
                }
                else if (request.TransactionsIds != null)
                {
                    IEnumerable<uint256> ids = request.TransactionsIds.Select(uint256.Parse);
                    result = this.walletManager.RemoveTransactionsByIds(request.WalletName, ids);
                }
                else
                {
                    throw new WalletException("A filter specifying what transactions to remove must be set.");
                }

                // If the user chose to resync the wallet after removing transactions.
                if (request.ReSync)
                {
                    Wallet wallet = this.walletManager.GetWallet(request.WalletName);

                    // Initiate the scan one day ahead of wallet creation.
                    // If the creation time is DateTime.MinValue, don't remove one day as that throws an exception.
                    ChainedHeader chainedHeader = this.chainIndexer.GetHeader(this.chainIndexer.GetHeightAtTime(wallet.CreationTime.DateTime != DateTime.MinValue ? wallet.CreationTime.DateTime.AddDays(-1) : wallet.CreationTime.DateTime));

                    // Save the updated wallet to the file system.
                    this.walletManager.SaveWallet(wallet.Name);

                    // Start the sync from the day before the wallet was created.
                    this.walletSyncManager.SyncFromHeight(chainedHeader.Height, request.WalletName);
                }

                return result.Select(r => new RemovedTransactionModel
                {
                    TransactionId = r.transactionId,
                    CreationTime = r.creationTime
                });
            }, cancellationToken);
        }

        public async Task RemoveWallet(RemoveWalletModel request,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                this.walletManager.DeleteWallet(request.WalletName);
            }, cancellationToken);
        }

        public async Task<AddressesModel> GetAllAddresses(GetAllAddressesModel request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                Wallet wallet = this.walletManager.GetWallet(request.WalletName);
                HdAccount account = wallet.GetAccount(request.AccountName);
                if (account == null)
                    throw new WalletException($"No account with the name '{request.AccountName}' could be found.");

                // This only runs on one account at a time, so there is no need to make a distinction between normal and cold staking accounts.
                var accRef = new WalletAccountReference(request.WalletName, request.AccountName);

                var unusedNonChange = this.walletManager.GetUnusedAddresses(accRef, false)
                    .Select(a => (address: a, isUsed: false, isChange: false, confirmed: Money.Zero, total: Money.Zero))
                    .ToList();
                var unusedChange = this.walletManager.GetUnusedAddresses(accRef, true)
                    .Select(a => (address: a, isUsed: false, isChange: true, confirmed: Money.Zero, total: Money.Zero))
                    .ToList();
                var usedNonChange = this.walletManager.GetUsedAddresses(accRef, false)
                    .Select(a => (a.address, isUsed: true, isChange: false, a.confirmed, a.total)).ToList();
                var usedChange = this.walletManager.GetUsedAddresses(accRef, true)
                    .Select(a => (a.address, isUsed: true, isChange: true, a.confirmed, a.total)).ToList();

                return new AddressesModel()
                {
                    Addresses = unusedNonChange
                        .Concat(unusedChange)
                        .Concat(usedNonChange)
                        .Concat(usedChange)
                        .Select(a => new AddressModel
                        {
                            Address = a.address.Address,
                            IsUsed = a.isUsed,
                            IsChange = a.isChange,
                            AmountConfirmed = a.confirmed,
                            AmountUnconfirmed = a.total - a.confirmed
                        })
                };
            }, cancellationToken);
        }

        public async Task<WalletBuildTransactionModel> BuildTransaction(BuildTransactionRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await Task.Run(() =>
            {
                if (request.Recipients == null)
                {
                    request.Recipients = new List<RecipientModel>();
                }

                if (request.Recipients.Count == 0 && (request.OpReturnAmount == null || request.OpReturnAmount == Money.Zero))
                    throw new FeatureException(HttpStatusCode.BadRequest, "No recipients.", "Either one or both of recipients and opReturnAmount must be specified.");

                var recipients = new List<Recipient>();

                bool seenSubtractFlag = false;
                foreach (RecipientModel recipientModel in request.Recipients)
                {
                    if (string.IsNullOrWhiteSpace(recipientModel.DestinationAddress) && string.IsNullOrWhiteSpace(recipientModel.DestinationScript))
                        throw new FeatureException(HttpStatusCode.BadRequest, "No recipient address.", "Either a destination address or script must be specified.");

                    if (recipientModel.SubtractFeeFromAmount)
                    {
                        if (seenSubtractFlag)
                        {
                            throw new FeatureException(HttpStatusCode.BadRequest, "Multiple fee subtraction recipients.", "The transaction fee can only be removed from a single output.");
                        }

                        seenSubtractFlag = true;
                    }

                    Script destination;

                    if (!string.IsNullOrWhiteSpace(recipientModel.DestinationAddress))
                    {
                        destination = BitcoinAddress.Create(recipientModel.DestinationAddress, this.network).ScriptPubKey;
                    }
                    else
                    {
                        destination = Script.FromHex(recipientModel.DestinationScript);
                    }

                    recipients.Add(new Recipient
                    {
                        ScriptPubKey = destination,
                        Amount = recipientModel.Amount,
                        SubtractFeeFromAmount = recipientModel.SubtractFeeFromAmount
                    });
                }

                // If specified, get the change address, which must already exist in the wallet.
                HdAddress changeAddress = null;
                if (!string.IsNullOrWhiteSpace(request.ChangeAddress))
                {
                    Wallet wallet = this.walletManager.GetWallet(request.WalletName);
                    HdAccount account = wallet.GetAccount(request.AccountName);
                    if (account == null)
                    {
                        throw new FeatureException(HttpStatusCode.BadRequest, "Account not found.",
                            $"No account with the name '{request.AccountName}' could be found in wallet {wallet.Name}.");
                    }

                    changeAddress = account.GetCombinedAddresses()
                        .FirstOrDefault(x => x.Address == request.ChangeAddress);

                    if (changeAddress == null)
                    {
                        throw new FeatureException(HttpStatusCode.BadRequest, "Change address not found.",
                            $"No change address '{request.ChangeAddress}' could be found in wallet {wallet.Name}.");
                    }
                }

                var context = new TransactionBuildContext(this.network)
                {
                    AccountReference = new WalletAccountReference(request.WalletName, request.AccountName),
                    TransactionFee = string.IsNullOrEmpty(request.FeeAmount) ? null : Money.Parse(request.FeeAmount),
                    MinConfirmations = request.AllowUnconfirmed ? 0 : 1,
                    Shuffle = request.ShuffleOutputs ??
                              true, // We shuffle transaction outputs by default as it's better for anonymity.
                    OpReturnData = request.OpReturnData,
                    OpReturnAmount = string.IsNullOrEmpty(request.OpReturnAmount)
                        ? null
                        : Money.Parse(request.OpReturnAmount),
                    WalletPassword = request.Password,
                    SelectedInputs = request.Outpoints
                        ?.Select(u => new OutPoint(uint256.Parse(u.TransactionId), u.Index)).ToList(),
                    AllowOtherInputs = false,
                    Recipients = recipients,
                    ChangeAddress = changeAddress
                };

                if (!string.IsNullOrEmpty(request.FeeType))
                {
                    context.FeeType = FeeParser.Parse(request.FeeType);
                }

                Transaction transactionResult = this.walletTransactionHandler.BuildTransaction(context);

                return new WalletBuildTransactionModel
                {
                    Hex = transactionResult.ToHex(),
                    Fee = context.TransactionFee,
                    TransactionId = transactionResult.GetHash()
                };
            }, cancellationToken);
        }

        public async Task<Money> GetTransactionFeeEstimate(TxFeeEstimateRequest request,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var recipients = request.Recipients.Select(recipientModel => new Recipient
                {
                    ScriptPubKey = BitcoinAddress.Create(recipientModel.DestinationAddress, this.network).ScriptPubKey,
                    Amount = recipientModel.Amount
                }).ToList();

                var context = new TransactionBuildContext(this.network)
                {
                    AccountReference = new WalletAccountReference(request.WalletName, request.AccountName),
                    FeeType = FeeParser.Parse(request.FeeType),
                    MinConfirmations = request.AllowUnconfirmed ? 0 : 1,
                    Recipients = recipients,
                    OpReturnData = request.OpReturnData,
                    OpReturnAmount = string.IsNullOrEmpty(request.OpReturnAmount)
                        ? null
                        : Money.Parse(request.OpReturnAmount),
                    Sign = false
                };

                return this.walletTransactionHandler.EstimateFee(context);
            }, cancellationToken);
        }

        public async Task RecoverViaExtPubKey(WalletExtPubRecoveryRequest request, CancellationToken token)
        {
            await Task.Run(() =>
            {
                try
                {
                    string accountExtPubKey =
                        this.network.IsBitcoin()
                            ? request.ExtPubKey
                            : LegacyExtPubKeyConverter.ConvertIfInLegacyStratisFormat(request.ExtPubKey, this.network);

                    this.walletManager.RecoverWallet(request.Name, ExtPubKey.Parse(accountExtPubKey),
                        request.AccountIndex,
                        request.CreationDate, null);
                }
                catch (WalletException e)
                {
                    // Wallet already exists.
                    throw new FeatureException(HttpStatusCode.Conflict, e.Message, e.ToString());
                }
                catch (FileNotFoundException e)
                {
                    // Wallet does not exist.
                    throw new FeatureException(HttpStatusCode.NotFound, "Wallet not found.", e.ToString());
                }
            }, token);
        }

        public async Task RecoverWallet(WalletRecoveryRequest request, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!this.walletManager.IsStarted)
                        throw new WalletException("The wallet manager has not yet finished initializing.");

                    this.walletManager.RecoverWallet(request.Password, request.Name, request.Mnemonic,
                        request.CreationDate, passphrase: request.Passphrase);
                }
                catch (WalletException e)
                {
                    // indicates that this wallet already exists
                    throw new FeatureException(HttpStatusCode.Conflict, e.Message, e.ToString());
                }
                catch (FileNotFoundException e)
                {
                    // indicates that this wallet does not exist
                    throw new FeatureException(HttpStatusCode.NotFound, "Wallet not found.", e.ToString());
                }
            }, cancellationToken);
        }

        public async Task LoadWallet(WalletLoadRequest request, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    this.walletManager.LoadWallet(request.Password, request.Name);
                }
                catch (FileNotFoundException e)
                {
                    throw new FeatureException(HttpStatusCode.NotFound,
                        "This wallet was not found at the specified location.", e.ToString());
                }
                catch (WalletException e)
                {
                    throw new FeatureException(HttpStatusCode.NotFound,
                        "This wallet was not found at the specified location.", e.ToString());
                }
                catch (SecurityException e)
                {
                    // indicates that the password is wrong
                    throw new FeatureException(HttpStatusCode.Forbidden,
                        "Wrong password, please try again.",
                        e.ToString());
                }
            }, cancellationToken);
        }

        public async Task<MaxSpendableAmountModel> GetMaximumSpendableBalance(WalletMaximumBalanceRequest request,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                (Money maximumSpendableAmount, Money fee) = this.walletTransactionHandler.GetMaximumSpendableAmount(
                    new WalletAccountReference(request.WalletName, request.AccountName),
                    FeeParser.Parse(request.FeeType), request.AllowUnconfirmed);

                return new MaxSpendableAmountModel
                {
                    MaxSpendableAmount = maximumSpendableAmount,
                    Fee = fee
                };
            }, cancellationToken);
        }

        public async Task<SpendableTransactionsModel> GetSpendableTransactions(SpendableTransactionsRequest request,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                IEnumerable<UnspentOutputReference> spendableTransactions =
                    this.walletManager.GetSpendableTransactionsInAccount(
                        new WalletAccountReference(request.WalletName, request.AccountName), request.MinConfirmations);

                return new SpendableTransactionsModel
                {
                    SpendableTransactions = spendableTransactions.Select(st => new SpendableTransactionModel
                    {
                        Id = st.Transaction.Id,
                        Amount = st.Transaction.Amount,
                        Address = st.Address.Address,
                        Index = st.Transaction.Index,
                        IsChange = st.Address.IsChangeAddress(),
                        CreationTime = st.Transaction.CreationTime,
                        Confirmations = st.Confirmations
                    }).ToList()
                };
            }, cancellationToken);
        }

        public async Task<DistributeUtxoModel> DistributeUtxos(DistributeUtxosRequest request,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var model = new DistributeUtxoModel()
                {
                    WalletName = request.WalletName,
                    UseUniqueAddressPerUtxo = request.UseUniqueAddressPerUtxo,
                    UtxosCount = request.UtxosCount,
                    UtxoPerTransaction = request.UtxoPerTransaction,
                    TimestampDifferenceBetweenTransactions = request.TimestampDifferenceBetweenTransactions,
                    MinConfirmations = request.MinConfirmations,
                    DryRun = request.DryRun
                };

                var walletReference = new WalletAccountReference(request.WalletName, request.AccountName);

                Wallet wallet = this.walletManager.GetWallet(request.WalletName);
                HdAccount account = wallet.GetAccount(request.AccountName);

                var addresses = new List<HdAddress>();

                if (request.ReuseAddresses)
                {
                    addresses = this.walletManager.GetUnusedAddresses(walletReference,
                        request.UseUniqueAddressPerUtxo ? request.UtxosCount : 1, request.UseChangeAddresses).ToList();
                }
                else if (request.UseChangeAddresses)
                {
                    addresses = account.InternalAddresses.Take(request.UseUniqueAddressPerUtxo ? request.UtxosCount : 1)
                        .ToList();
                }
                else if (!request.UseChangeAddresses)
                {
                    addresses = account.ExternalAddresses.Take(request.UseUniqueAddressPerUtxo ? request.UtxosCount : 1)
                        .ToList();
                }

                IEnumerable<UnspentOutputReference> spendableTransactions =
                    this.walletManager.GetSpendableTransactionsInAccount(
                        new WalletAccountReference(request.WalletName, request.AccountName), request.MinConfirmations);

                if (request.Outpoints != null && request.Outpoints.Any())
                {
                    var selectedUnspentOutputReferenceList = new List<UnspentOutputReference>();
                    foreach (UnspentOutputReference unspentOutputReference in spendableTransactions)
                    {
                        if (request.Outpoints.Any(o =>
                            o.TransactionId == unspentOutputReference.Transaction.Id.ToString() &&
                            o.Index == unspentOutputReference.Transaction.Index))
                        {
                            selectedUnspentOutputReferenceList.Add(unspentOutputReference);
                        }
                    }

                    spendableTransactions = selectedUnspentOutputReferenceList;
                }

                int totalOutpointCount = spendableTransactions.Count();
                int calculatedTransactionCount = request.UtxosCount / request.UtxoPerTransaction;
                int inputsPerTransaction = totalOutpointCount / calculatedTransactionCount;

                if (calculatedTransactionCount > totalOutpointCount)
                {
                    this.logger.LogError(
                        $"You have requested to create {calculatedTransactionCount} transactions but there are only {totalOutpointCount} UTXOs in the wallet. Number of transactions which could be created has to be lower than total number of UTXOs in the wallet. If higher number of transactions is required please first distibute funds to create larget set of UTXO and retry this operation.");
                    throw new FeatureException(HttpStatusCode.BadRequest, "Invalid parameters", "Invalid parameters");
                }

                var recipients = new List<Recipient>(request.UtxosCount);
                int addressIndex = 0;
                var transactionList = new List<Transaction>();

                for (int i = 0; i < request.UtxosCount; i++)
                {
                    recipients.Add(new Recipient { ScriptPubKey = addresses[addressIndex].ScriptPubKey });

                    if (request.UseUniqueAddressPerUtxo)
                        addressIndex++;

                    if ((i + 1) % request.UtxoPerTransaction == 0 || i == request.UtxosCount - 1)
                    {
                        var transactionTransferAmount = new Money(0);
                        var inputs = new List<OutPoint>();

                        foreach (UnspentOutputReference unspentOutputReference in spendableTransactions
                            .Skip(transactionList.Count * inputsPerTransaction).Take(inputsPerTransaction))
                        {
                            inputs.Add(new OutPoint(unspentOutputReference.Transaction.Id,
                                unspentOutputReference.Transaction.Index));
                            transactionTransferAmount += unspentOutputReference.Transaction.Amount;
                        }

                        // Add any remaining UTXOs to the last transaction.
                        if (i == request.UtxosCount - 1)
                        {
                            foreach (UnspentOutputReference unspentOutputReference in spendableTransactions.Skip(
                                (transactionList.Count + 1) * inputsPerTransaction))
                            {
                                inputs.Add(new OutPoint(unspentOutputReference.Transaction.Id,
                                    unspentOutputReference.Transaction.Index));
                                transactionTransferAmount += unspentOutputReference.Transaction.Amount;
                            }
                        }

                        // For the purpose of fee estimation use the transfer amount as if the fee were network.MinTxFee.
                        Money transferAmount = (transactionTransferAmount) / recipients.Count;
                        recipients.ForEach(r => r.Amount = transferAmount);

                        var context = new TransactionBuildContext(this.network)
                        {
                            AccountReference = walletReference,
                            Shuffle = false,
                            WalletPassword = request.WalletPassword,
                            Recipients = recipients,
                            Time = (uint)this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp() +
                                   (uint)request.TimestampDifferenceBetweenTransactions,
                            AllowOtherInputs = false,
                            SelectedInputs = inputs,
                            FeeType = FeeType.Low
                        };

                        // Set the amount once we know how much the transfer will cost.
                        Money transactionFee;
                        try
                        {
                            Transaction transaction = this.walletTransactionHandler.BuildTransaction(context);

                            // Due to how the code works the line below is probably never used.
                            var transactionSize = transaction.GetSerializedSize();
                            transactionFee = new FeeRate(this.network.MinTxFee).GetFee(transactionSize);
                        }
                        catch (NotEnoughFundsException ex)
                        {
                            // This remains the best approach for estimating transaction fees.
                            transactionFee = (Money)ex.Missing;
                        }

                        if (transactionFee < this.network.MinTxFee)
                            transactionFee = new Money(this.network.MinTxFee);

                        transferAmount = (transactionTransferAmount - transactionFee) / recipients.Count;
                        recipients.ForEach(r => r.Amount = transferAmount);

                        context = new TransactionBuildContext(this.network)
                        {
                            AccountReference = walletReference,
                            Shuffle = false,
                            WalletPassword = request.WalletPassword,
                            Recipients = recipients,
                            Time = (uint)this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp() +
                                   (uint)request.TimestampDifferenceBetweenTransactions,
                            AllowOtherInputs = false,
                            SelectedInputs = inputs,
                            TransactionFee = transactionFee
                        };

                        Transaction transactionResult = this.walletTransactionHandler.BuildTransaction(context);
                        transactionList.Add(transactionResult);
                        recipients = new List<Recipient>();
                    }
                }

                foreach (Transaction transaction in transactionList)
                {
                    var modelItem = new WalletSendTransactionModel
                    {
                        TransactionId = transaction.GetHash(),
                        Outputs = new List<TransactionOutputModel>()
                    };

                    foreach (TxOut output in transaction.Outputs)
                    {
                        bool isUnspendable = output.ScriptPubKey.IsUnspendable;
                        modelItem.Outputs.Add(new TransactionOutputModel
                        {
                            Address = isUnspendable
                                ? null
                                : output.ScriptPubKey.GetDestinationAddress(this.network)?.ToString(),
                            Amount = output.Value,
                            OpReturnData = isUnspendable
                                ? Encoding.UTF8.GetString(output.ScriptPubKey.ToOps().Last().PushData)
                                : null
                        });
                    }

                    model.WalletSendTransaction.Add(modelItem);

                    if (!request.DryRun)
                    {
                        this.broadcasterManager.BroadcastTransactionAsync(transaction).GetAwaiter().GetResult();

                        TransactionBroadcastEntry transactionBroadCastEntry =
                            this.broadcasterManager.GetTransaction(transaction.GetHash());

                        if (transactionBroadCastEntry.TransactionBroadcastState == TransactionBroadcastState.CantBroadcast)
                        {
                            this.logger.LogError("Exception occurred: {0}", transactionBroadCastEntry.ErrorMessage);
                            throw new FeatureException(HttpStatusCode.BadRequest,
                                transactionBroadCastEntry.ErrorMessage,
                                "Transaction Exception");
                        }
                    }
                }

                return model;
            }, cancellationToken);
        }

        public async Task<List<string>> Sweep(SweepRequest request, CancellationToken cancellationToken)
        {
            // Build the set of scriptPubKeys to look for.
            var scriptList = new HashSet<Script>();

            var keyMap = new Dictionary<Script, Key>();

            // Currently this is only designed to support P2PK and P2PKH, although segwit scripts are probably easily added.
            foreach (string wif in request.PrivateKeys)
            {
                var privateKey = Key.Parse(wif, this.network);

                Script p2pk = PayToPubkeyTemplate.Instance.GenerateScriptPubKey(privateKey.PubKey);
                Script p2pkh = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(privateKey.PubKey);

                keyMap.Add(p2pk, privateKey);
                keyMap.Add(p2pkh, privateKey);

                scriptList.Add(p2pk);
                scriptList.Add(p2pkh);
            }

            return await Task.Run(() =>
            {
                var coinView = this.utxoIndexer.GetCoinviewAtHeight(this.chainIndexer.Height);

                var builder = new TransactionBuilder(this.network);

                var sweepTransactions = new List<string>();

                Money total = 0;
                int currentOutputCount = 0;

                foreach (OutPoint outPoint in coinView.UnspentOutputs)
                {
                    // Obtain the transaction output in question.
                    TxOut txOut = coinView.Transactions[outPoint.Hash].Outputs[outPoint.N];

                    // Check if the scriptPubKey matches one of those for the supplied private keys.
                    if (!scriptList.Contains(txOut.ScriptPubKey))
                    {
                        continue;
                    }

                    // Add the UTXO as an input to the sweeping transaction.
                    builder.AddCoins(new Coin(outPoint, txOut));
                    builder.AddKeys(new[] { keyMap[txOut.ScriptPubKey] });

                    currentOutputCount++;
                    total += txOut.Value;

                    // Not many wallets will have this many inputs, but we have to ensure that the resulting transactions are
                    // small enough to be broadcast without standardness problems.
                    // Since there is only 1 output the size of the inputs is the only consideration.
                    if (total == 0 || currentOutputCount < 500)
                        continue;

                    BitcoinAddress destination = BitcoinAddress.Create(request.DestinationAddress, this.network);

                    builder.Send(destination, total);

                    // Cause the last destination to pay the fee, as we have no other funds to pay fees with.
                    builder.SubtractFees();

                    FeeRate feeRate = this.walletFeePolicy.GetFeeRate(FeeType.High.ToConfirmations());
                    builder.SendEstimatedFees(feeRate);

                    Transaction sweepTransaction = builder.BuildTransaction(true);

                    TransactionPolicyError[] errors = builder.Check(sweepTransaction);

                    // TODO: Perhaps return a model with an errors property to inform the user
                    if (errors.Length == 0)
                        sweepTransactions.Add(sweepTransaction.ToHex());

                    // Reset the builder and related state, as we are now creating a fresh transaction.
                    builder = new TransactionBuilder(this.network);

                    currentOutputCount = 0;
                    total = 0;
                }

                if (sweepTransactions.Count == 0)
                    return sweepTransactions;

                if (request.Broadcast)
                {
                    foreach (string sweepTransaction in sweepTransactions)
                    {
                        Transaction toBroadcast = this.network.CreateTransaction(sweepTransaction);

                        this.broadcasterManager.BroadcastTransactionAsync(toBroadcast).GetAwaiter().GetResult();
                    }
                }

                return sweepTransactions;
            }, cancellationToken);
        }

        public async Task<BuildOfflineSignResponse> BuildOfflineSignRequest(BuildOfflineSignRequest request, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                // TODO: It might make more sense to pull out the common code between this and an online transaction build into a common method
                if (request.Recipients == null)
                {
                    request.Recipients = new List<RecipientModel>();
                }

                if (request.Recipients.Count == 0 && (request.OpReturnAmount == null || request.OpReturnAmount == Money.Zero))
                    throw new FeatureException(HttpStatusCode.BadRequest, "No recipients.", "Either one or both of recipients and opReturnAmount must be specified.");

                var recipients = new List<Recipient>();

                bool seenSubtractFlag = false;
                foreach (RecipientModel recipientModel in request.Recipients)
                {
                    if (string.IsNullOrWhiteSpace(recipientModel.DestinationAddress) && string.IsNullOrWhiteSpace(recipientModel.DestinationScript))
                        throw new FeatureException(HttpStatusCode.BadRequest, "No recipient address.", "Either a destination address or script must be specified.");

                    if (recipientModel.SubtractFeeFromAmount)
                    {
                        if (seenSubtractFlag)
                        {
                            throw new FeatureException(HttpStatusCode.BadRequest, "Multiple fee subtraction recipients.", "The transaction fee can only be removed from a single output.");
                        }

                        seenSubtractFlag = true;
                    }

                    Script destination;

                    if (!string.IsNullOrWhiteSpace(recipientModel.DestinationAddress))
                    {
                        destination = BitcoinAddress.Create(recipientModel.DestinationAddress, this.network).ScriptPubKey;
                    }
                    else
                    {
                        destination = Script.FromHex(recipientModel.DestinationScript);
                    }

                    recipients.Add(new Recipient
                    {
                        ScriptPubKey = destination,
                        Amount = recipientModel.Amount,
                        SubtractFeeFromAmount = recipientModel.SubtractFeeFromAmount
                    });
                }

                // If specified, get the change address, which must already exist in the wallet.
                HdAddress changeAddress = null;
                if (!string.IsNullOrWhiteSpace(request.ChangeAddress))
                {
                    Wallet wallet = this.walletManager.GetWallet(request.WalletName);
                    HdAccount account = wallet.GetAccount(request.AccountName);
                    if (account == null)
                    {
                        throw new FeatureException(HttpStatusCode.BadRequest, "Account not found.",
                            $"No account with the name '{request.AccountName}' could be found in wallet {wallet.Name}.");
                    }

                    changeAddress = account.GetCombinedAddresses()
                        .FirstOrDefault(x => x.Address == request.ChangeAddress);

                    if (changeAddress == null)
                    {
                        throw new FeatureException(HttpStatusCode.BadRequest, "Change address not found.",
                            $"No change address '{request.ChangeAddress}' could be found in wallet {wallet.Name}.");
                    }
                }

                var context = new TransactionBuildContext(this.network)
                {
                    AccountReference = new WalletAccountReference(request.WalletName, request.AccountName),
                    TransactionFee = string.IsNullOrEmpty(request.FeeAmount) ? null : Money.Parse(request.FeeAmount),
                    MinConfirmations = request.AllowUnconfirmed ? 0 : 1,
                    Shuffle = request.ShuffleOutputs ??
                              true, // We shuffle transaction outputs by default as it's better for anonymity.
                    OpReturnData = request.OpReturnData,
                    OpReturnAmount = string.IsNullOrEmpty(request.OpReturnAmount)
                        ? null
                        : Money.Parse(request.OpReturnAmount),
                    SelectedInputs = request.Outpoints
                        ?.Select(u => new OutPoint(uint256.Parse(u.TransactionId), u.Index)).ToList(),
                    AllowOtherInputs = false,
                    Recipients = recipients,
                    ChangeAddress = changeAddress,

                    Sign = false
                };

                if (!string.IsNullOrEmpty(request.FeeType))
                {
                    context.FeeType = FeeParser.Parse(request.FeeType);
                }

                Transaction transactionResult = this.walletTransactionHandler.BuildTransaction(context);

                // Need to be able to look up the keypath for the UTXOs that were used.
                IEnumerable<UnspentOutputReference> spendableTransactions =
                    this.walletManager.GetSpendableTransactionsInAccount(
                        new WalletAccountReference(request.WalletName, request.AccountName), 0).ToList();

                var utxos = new List<UtxoDescriptor>();
                var addresses = new List<AddressDescriptor>();
                foreach (ICoin coin in context.TransactionBuilder.FindSpentCoins(transactionResult))
                {
                    utxos.Add(new UtxoDescriptor()
                    {
                        Amount = coin.TxOut.Value.ToUnit(MoneyUnit.BTC).ToString(),
                        TransactionId = coin.Outpoint.Hash.ToString(),
                        Index = coin.Outpoint.N.ToString(),
                        ScriptPubKey = coin.TxOut.ScriptPubKey.ToHex()
                    });

                    UnspentOutputReference outputReference = spendableTransactions.FirstOrDefault(u => u.Transaction.Id == coin.Outpoint.Hash && u.Transaction.Index == coin.Outpoint.N);

                    // TODO: This should never really be null. But the address list is regarded as optional hinting data so it's not a critical failure
                    if (outputReference != null)
                    {
                        bool segwit = outputReference.Transaction.ScriptPubKey.IsScriptType(ScriptType.P2WPKH);
                        addresses.Add(new AddressDescriptor() { Address = segwit ? outputReference.Address.Bech32Address : outputReference.Address.Address, AddressType = segwit ? "p2wpkh" : "p2pkh", KeyPath = outputReference.Address.HdPath });
                    }
                }

                // Return transaction hex, UTXO list, address list
                return new BuildOfflineSignResponse()
                {
                    WalletName = request.WalletName,
                    WalletAccount = request.AccountName,
                    Fee = context.TransactionFee.ToUnit(MoneyUnit.BTC).ToString(),
                    UnsignedTransaction = transactionResult.ToHex(),
                    Utxos = utxos,
                    Addresses = addresses
                };
            }, cancellationToken);
        }

        public virtual async Task<WalletBuildTransactionModel> OfflineSignRequest(OfflineSignRequest request, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                Transaction unsignedTransaction = this.network.CreateTransaction(request.UnsignedTransaction);

                uint256 originalTxId = unsignedTransaction.GetHash();

                var builder = new TransactionBuilder(this.network);
                var coins = new List<Coin>();
                var signingKeys = new List<ISecret>();

                ExtKey seedExtKey = this.walletManager.GetExtKey(new WalletAccountReference() { AccountName = request.WalletAccount, WalletName = request.WalletName }, request.WalletPassword);

                // Have to determine which private key to use for each UTXO being spent.
                foreach (UtxoDescriptor utxo in request.Utxos)
                {
                    Script scriptPubKey = Script.FromHex(utxo.ScriptPubKey);

                    coins.Add(new Coin(uint256.Parse(utxo.TransactionId), uint.Parse(utxo.Index), Money.Parse(utxo.Amount), scriptPubKey));

                    // Now try get the associated private key. We therefore need to determine the address that contains the UTXO.
                    string address = scriptPubKey.GetDestinationAddress(this.network).ToString();
                    var accounts = this.walletManager.GetAccounts(request.WalletName);
                    HdAddress hdAddress = accounts.SelectMany(hdAccount => hdAccount.GetCombinedAddresses()).FirstOrDefault(a => a.Address == address || a.Bech32Address == address);

                    // It is possible that the address is outside the gap limit. So if it is not found we optimistically presume the address descriptors will fill in the missing information later.
                    if (hdAddress != null)
                    {
                        ExtKey addressExtKey = seedExtKey.Derive(new KeyPath(hdAddress.HdPath));
                        BitcoinExtKey addressPrivateKey = addressExtKey.GetWif(this.network);
                        signingKeys.Add(addressPrivateKey);
                    }
                }

                // Address descriptors are 'easier' to look the private key up against if provided, but may not always be available.
                foreach (AddressDescriptor address in request.Addresses)
                {
                    ExtKey addressExtKey = seedExtKey.Derive(new KeyPath(address.KeyPath));
                    BitcoinExtKey addressPrivateKey = addressExtKey.GetWif(this.network);
                    signingKeys.Add(addressPrivateKey);
                }

                builder.AddCoins(coins);
                builder.AddKeys(signingKeys.ToArray());
                builder.SignTransactionInPlace(unsignedTransaction);

                // TODO: Do something with the errors
                if (!builder.Verify(unsignedTransaction, out TransactionPolicyError[] errors))
                {
                    throw new FeatureException(HttpStatusCode.BadRequest, "Failed to validate signed transaction.",
                        $"Failed to validate signed transaction '{unsignedTransaction.GetHash()}' from offline request '{originalTxId}'.");
                }

                var builtTransactionModel = new WalletBuildTransactionModel() { TransactionId = unsignedTransaction.GetHash(), Hex = unsignedTransaction.ToHex(), Fee = request.Fee };

                return builtTransactionModel;
            }, cancellationToken);
        }

        public async Task<string> Consolidate(ConsolidationRequest request, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var utxos = new List<UnspentOutputReference>();
                var accountReference = new WalletAccountReference(request.WalletName, request.AccountName);

                if (!string.IsNullOrWhiteSpace(request.SingleAddress))
                {
                    utxos = this.walletManager.GetSpendableTransactionsInWallet(request.WalletName, 1).Where(u => u.Address.Address == request.SingleAddress || u.Address.Address == request.SingleAddress).OrderBy(u2 => u2.Transaction.Amount).ToList();
                }
                else
                {
                    utxos = this.walletManager.GetSpendableTransactionsInAccount(accountReference, 1).OrderBy(u2 => u2.Transaction.Amount).ToList();
                }

                if (utxos.Count == 0)
                {
                    throw new FeatureException(HttpStatusCode.BadRequest, "Failed to locate any unspent outputs to consolidate.",
                        "Failed to locate any unspent outputs to consolidate.");
                }

                if (!string.IsNullOrWhiteSpace(request.UtxoValueThreshold))
                {
                    var threshold = Money.Parse(request.UtxoValueThreshold);

                    utxos = utxos.Where(u => u.Transaction.Amount <= threshold).ToList();
                }

                if (utxos.Count == 0)
                {
                    throw new FeatureException(HttpStatusCode.BadRequest, "After filtering for size, failed to locate any unspent outputs to consolidate.",
                        "After filtering for size, failed to locate any unspent outputs to consolidate.");
                }

                if (utxos.Count == 1)
                {
                    throw new FeatureException(HttpStatusCode.BadRequest, "Already consolidated.",
                        "Already consolidated.");
                }

                Script destination;
                if (!string.IsNullOrWhiteSpace(request.DestinationAddress))
                {
                    destination = BitcoinAddress.Create(request.DestinationAddress, this.network).ScriptPubKey;
                }
                else
                {
                    destination = this.walletManager.GetUnusedAddress(accountReference).ScriptPubKey;
                }

                // Set the maximum upper bound at 1000, as we don't expect any transaction can ever have that many inputs and still
                // be under the size limit.
                int upperBound = Math.Min(utxos.Count, 1000);
                int lowerBound = 0;
                int iterations = 0;

                // Assuming a worst case binary search of log2(1000) + 1.
                // Mostly we just want to bound the attempts.
                while (iterations < 11)
                {
                    if (lowerBound == upperBound)
                        break;

                    // We perform a form of binary search through the UTXO list in order to find a reasonable number of UTXOs to include for consolidation.
                    int candidate = (lowerBound + upperBound) / 2;

                    // When the values of the bounds start getting too close together, just short circuit further checks.
                    if (candidate == lowerBound || candidate == upperBound)
                        break;

                    // First check if (lower + upper) / 2 is under the size limit and move the lower bound upwards if it is.
                    int size = this.GetTransactionSizeForUtxoCount(utxos, candidate, accountReference, destination);

                    // If the midpoint resulted in a transaction that was too big then move the upper bound downwards.
                    if (this.SizeAcceptable(size))
                        lowerBound = candidate;
                    else
                        upperBound = candidate;

                    iterations++;
                }

                // This is exceedingly unlikely unless the smallest-value UTXO was gigantic.
                if (lowerBound == 0)
                    throw new FeatureException(HttpStatusCode.BadRequest, "Unable to consolidate.",
                        "Unable to consolidate.");

                // lowerBound will have converged to approximately the highest possible acceptable UTXO count.
                List<UnspentOutputReference> finalUtxos = utxos.Take(lowerBound).ToList();
                List<OutPoint> outpoints = finalUtxos.Select(u => u.ToOutPoint()).ToList();
                Money totalToSend = finalUtxos.Sum(a => a.Transaction.Amount);

                // Build the final version of the consolidation transaction.
                var context = new TransactionBuildContext(this.network)
                {
                    AccountReference = accountReference,
                    AllowOtherInputs = false,
                    FeeType = FeeType.Medium,
                    Recipients = new List<Recipient>() { new Recipient() { ScriptPubKey = destination, Amount = totalToSend, SubtractFeeFromAmount = true } },
                    SelectedInputs = outpoints,
                    WalletPassword = request.WalletPassword,

                    Sign = true
                };

                Transaction transaction = this.walletTransactionHandler.BuildTransaction(context);

                if (request.Broadcast)
                {
                    this.broadcasterManager.BroadcastTransactionAsync(transaction);
                }

                return transaction.ToHex();
            }, cancellationToken);
        }

        private bool SizeAcceptable(int size)
        {
            // Leave a bit of an error margin for size estimates that are not completely correct.
            return size <= (0.95m * this.network.Consensus.Options.MaxStandardTxWeight);
        }

        private int GetTransactionSizeForUtxoCount(List<UnspentOutputReference> utxos, int targetCount, WalletAccountReference accountReference, Script destination)
        {
            Money totalToSend = Money.Zero;
            var outpoints = new List<OutPoint>();

            int count = 0;
            foreach (UnspentOutputReference utxo in utxos)
            {
                if (count >= targetCount)
                    break;

                totalToSend += utxo.Transaction.Amount;
                outpoints.Add(utxo.ToOutPoint());
                count++;
            }

            var context = new TransactionBuildContext(this.network)
            {
                AccountReference = accountReference,
                AllowOtherInputs = false,
                FeeType = FeeType.Medium,
                // Prevent mempool transactions from being considered for inclusion.
                MinConfirmations = 1,
                // It is intended that consolidation should result in no change address, so the fee has to be subtracted from the single recipient.
                Recipients = new List<Recipient>() { new Recipient() { ScriptPubKey = destination, Amount = totalToSend, SubtractFeeFromAmount = true } },
                SelectedInputs = outpoints,

                Sign = false
            };

            // Note that this is the virtual size taking the witness scale factor of the current network into account, and not the raw byte count.
            return this.walletTransactionHandler.EstimateSize(context);
        }
    }
}
