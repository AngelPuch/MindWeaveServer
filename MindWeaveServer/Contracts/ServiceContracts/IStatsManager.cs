﻿using MindWeaveServer.Contracts.DataContracts;
using System.Collections.Generic;
using System.ServiceModel;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(IStatsCallback))]
    public interface IStatsManager
    {
        [OperationContract]
        PlayerStatsDto getPlayerStats(string username);

        [OperationContract]
        List<MatchHistoryDto> getMatchHistory(string username, int pageNumber, int pageSize);
    }

    [ServiceContract]
    public interface IStatsCallback
    {
        [OperationContract(IsOneWay = true)]
        void notifyStatsUpdated(PlayerStatsDto newStatsDto);
    }
}
