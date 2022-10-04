namespace SuggestionAppLibrary.DataAccess;

public interface IUserData
{
   Task CreateUser(UserModel user);
   Task<UserModel> GetUser(string id);
   Task<List<UserModel>> GetUsersAsync();
   Task<UserModel> GetUserFromAuthentication(string objectId);
   Task UpdateUser(UserModel user);
}