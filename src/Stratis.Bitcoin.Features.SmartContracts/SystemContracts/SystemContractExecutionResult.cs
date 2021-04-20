namespace Stratis.Bitcoin.Features.SmartContracts.SystemContracts
{
    public class SystemContractExecutionResult
    {
        public SystemContractExecutionResult(object result)
        {
            this.Result = result;
        }

        object Result { get; }
    }

}
