namespace Stratis.Bitcoin.Features.SmartContracts.SystemContracts
{
    public class SystemContractContext
    {
        public SystemContractContext(int blockHeight)
        {
            this.BlockHeight = blockHeight;
        }

        public int BlockHeight { get; }
    }

}
