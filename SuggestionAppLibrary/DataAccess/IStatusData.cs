namespace SuggestionAppLibrary.DataAccess;

public interface IStatusData
{
   Task<List<StatusModel>> GetAllStatuses();
   Task CreateStatuses(StatusModel status);
}