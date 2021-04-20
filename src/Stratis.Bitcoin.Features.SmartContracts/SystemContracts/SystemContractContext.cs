using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.SmartContracts.SystemContracts
{
    public class SystemContractContext
    {
        public SystemContractContext(int blockHeight, ContractTxData txData)
        {
            this.BlockHeight = blockHeight;
            this.TxData = txData;
        }

        public int BlockHeight { get; }
        public ContractTxData TxData { get; }
    }

}
