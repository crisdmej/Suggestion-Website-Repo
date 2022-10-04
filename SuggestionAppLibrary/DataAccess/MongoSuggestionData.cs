using Microsoft.Extensions.Caching.Memory;

namespace SuggestionAppLibrary.DataAccess;
public class MongoSuggestionData : ISuggestionData
{
	private readonly IDbConnection _db;
	private readonly IUserData _userData;
	private readonly IMemoryCache _cache;
	private readonly IMongoCollection<SuggestionModel> _suggestions;
	private const string CacheName = "SuggestionData";

	public MongoSuggestionData(IDbConnection db, IUserData userdata, IMemoryCache cache)
	{
		_db = db;
		_userData = userdata;
		_cache = cache;
		_suggestions = db.SuggestionCollection;
	}

	public async Task<List<SuggestionModel>> GetAllSuggestions()
	{
		var output = _cache.Get<List<SuggestionModel>>(CacheName);

		if (output == null)
		{
			var result = await _suggestions.FindAsync(s => s.Archive == false);
			output = result.ToList();

			_cache.Set(CacheName, output, TimeSpan.FromMinutes(1));
		}

		return output;
	}

	public async Task<List<SuggestionModel>> GetUsersSuggestions(string userId)
	{
		var output = _cache.Get<List<SuggestionModel>>(userId);
		if(output is null)
		{
			var results = await _suggestions.FindAsync(s => s.Author.Id == userId);
			output = results.ToList();

			_cache.Set(userId, output, TimeSpan.FromMinutes(1));
		}

		return output;
	}

	public async Task<List<SuggestionModel>> GetAllApprovedSuggestion()
	{
		var output = await GetAllSuggestions();
		return output.Where(x => x.ApprovedForRelease).ToList();
	}

	public async Task<SuggestionModel> GetSuggestion(string id)
	{
		var results = await _suggestions.FindAsync(s => s.Id == id);
		return results.FirstOrDefault();
	}

	public async Task<List<SuggestionModel>> GetAllSuggestionsWaitingForApproval()
	{
		var output = await GetAllSuggestions();
		return output.Where(x => x.ApprovedForRelease == false && x.Rejected == false).ToList();
	}

	public async Task UpdateSuggestion(SuggestionModel suggestion)
	{
		await _suggestions.ReplaceOneAsync(s => s.Id == suggestion.Id, suggestion);
		_cache.Remove(CacheName);
	}

	public async Task UpvoteSuggestion(string suggestionId, string userId)
	{
		var client = _db.Client;

		using var session = await client.StartSessionAsync();

		session.StartTransaction();

		try
		{
			var db = client.GetDatabase(_db.DbName);
			var suggestionsInTransactions = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
			var suggestion = (await suggestionsInTransactions.FindAsync(s => s.Id == suggestionId)).First();

			bool isUpvote = suggestion.UserVotes.Add(userId);
			if (isUpvote == false)
			{
				suggestion.UserVotes.Remove(userId);
			}

			await suggestionsInTransactions.ReplaceOneAsync(session, s => s.Id == suggestionId, suggestion);

			var usersInTranscations = db.GetCollection<UserModel>(_db.UserCollectionName);
			var user = await _userData.GetUser(userId);

			if (isUpvote)
			{
				user.VotedOnSuggestions.Add(new BasicSuggestionModel(suggestion));
			}
			else
			{
				var suggestionToRemove = user.VotedOnSuggestions.Where(s => s.Id == suggestionId).First();
				user.VotedOnSuggestions.Remove(suggestionToRemove);
			}
			await usersInTranscations.ReplaceOneAsync(session, u => u.Id == userId, user);

			await session.CommitTransactionAsync();

			_cache.Remove(CacheName);
		}
		catch (Exception ex)
		{
			await session.AbortTransactionAsync();
			throw;
		}
	}

	public async Task CreateSuggestion(SuggestionModel suggestion)
	{
		var client = _db.Client;

		using var session = await client.StartSessionAsync();

		session.StartTransaction();

		try
		{
			var db = client.GetDatabase(_db.DbName);
			var suggestionsInTransactions = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
			await suggestionsInTransactions.InsertOneAsync(session, suggestion);

			var usersInTrasactions = db.GetCollection<UserModel>(_db.UserCollectionName);
			var user = await _userData.GetUser(suggestion.Author.Id);
			user.AuthoredSuggestions.Add(new BasicSuggestionModel(suggestion));
			await usersInTrasactions.ReplaceOneAsync(session, u => u.Id == user.Id, user);

			await session.CommitTransactionAsync();

		}
		catch (Exception ex)
		{
			await session.AbortTransactionAsync();
			throw;
		}
	}
}
