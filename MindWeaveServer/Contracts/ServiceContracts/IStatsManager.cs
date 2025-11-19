using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Stats;
using System.Collections.Generic;
using System.ServiceModel;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(IStatsCallback))]
    public interface IStatsManager
    {
        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        PlayerStatsDto getPlayerStats(string username);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        List<MatchHistoryDto> getMatchHistory(string username, int pageNumber, int pageSize);
    }

    [ServiceContract]
    public interface IStatsCallback
    {
        [OperationContract(IsOneWay = true)]
        void notifyStatsUpdated(PlayerStatsDto newStatsDto);
    }
}
