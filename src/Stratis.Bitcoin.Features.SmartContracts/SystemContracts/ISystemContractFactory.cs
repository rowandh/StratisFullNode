using NBitcoin;

namespace Stratis.Bitcoin.Features.SmartContracts.SystemContracts
{
    public interface ISystemContractFactory
    {
        Network Network { get; }

        ISystemContract Create(string name);
    }
}