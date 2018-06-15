using Microsoft.WindowsAzure.Storage.Table;

namespace DetectionApp
{
    public class EndTimeInTable : TableEntity
    {
        public void AssignRowKey()
        {
            this.RowKey = "lastEndTime";
        }
        public void AssignPartitionKey()
        {
            this.PartitionKey = ProgramId;
        }
        public string ProgramId { get; set; }
        public string LastEndTime { get; set; }
        public string Id { get; set; }
        public string ProgramState { get; set; }
    }
}
