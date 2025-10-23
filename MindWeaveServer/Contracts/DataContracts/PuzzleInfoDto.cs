﻿using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts 
{
    [DataContract]
    public class PuzzleInfoDto
    {
        [DataMember]
        public int puzzleId { get; set; }

        [DataMember]
        public string name { get; set; }

        [DataMember]
        public string imagePath { get; set; } 
    }
}