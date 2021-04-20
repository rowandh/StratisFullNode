using System;
using System.Collections.Generic;
using System.Text;

namespace Stratis.Bitcoin.Features.SmartContracts.SystemContracts
{
    public interface ISystemContractExecutor
    {
        SystemContractExecutionResult Execute(SystemContractContext context);
    }
}
