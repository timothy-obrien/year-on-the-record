using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var root = app.Environment.ContentRootPath;
var dataDir = Path.Combine(root, "App_Data");
Directory.CreateDirectory(dataDir);
var storePath = Path.Combine(dataDir, "state.json");
var tracks = JsonSerializer.Deserialize<List<JsonElement[]>>(File.ReadAllText(Path.Combine(root, "songs.json")), json)!;
var store = File.Exists(storePath) ? JsonSerializer.Deserialize<Store>(File.ReadAllText(storePath), json)! : new Store();
var gate = new object();
string Token(int n = 24) => Convert.ToHexString(RandomNumberGenerator.GetBytes(n)).ToLowerInvariant();
void Save() { var tmp = storePath + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(store, json)); File.Move(tmp, storePath, true); }
object State(Room r, Player me) {
 object? song = null;
 if (r.Deck.Count > 0) { var t = tracks[r.Deck[r.Round]]; song = new { title=t[0].GetString(), artist=t[1].GetString(), spotifyId=t[3].GetString() }; }
 return new { code=r.Code, mode=r.Mode, minYear=r.MinYear, maxYear=r.MaxYear, status=r.Status, round=r.Round+1, players=r.Players.Select(p=>new { id=p.Id,name=p.Name,score=p.Score,submitted=r.Guesses.ContainsKey(p.Id) }).ToArray(), me=me.Id, isHost=r.Host==me.Id, track=song, reveal=r.Status is "reveal" or "finished" ? r.Reveal : null };
}
void Reveal(Room r) {
 int year=tracks[r.Deck[r.Round]][2].GetInt32();
 var distances=r.Players.Where(p=>r.Guesses.ContainsKey(p.Id)).Select(p=>Math.Abs(r.Guesses[p.Id]-year)).ToArray();
 int best=distances.Length>0?distances.Min():int.MaxValue;
 var results=new List<RoundResult>();
 foreach(var p in r.Players) { int? guess=r.Guesses.TryGetValue(p.Id,out int g)?g:null; int? diff=guess.HasValue?Math.Abs(guess.Value-year):null; int points=!diff.HasValue?0:r.Mode=="points"?Math.Max(0,100-diff.Value*10):diff.Value==best?1:0; p.Score+=points; results.Add(new(p.Id,p.Name,guess,points)); }
 r.Reveal=new(year,results);r.Status="reveal";
}
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapPost("/api", (Command d) => { lock(gate) { try {
 if(d.Action=="leaderboard") return Results.Json(new { scores=store.Scores.GroupBy(s=>s.Mode).SelectMany(g=>g.OrderByDescending(s=>s.Score).Take(100)).ToArray() });
 if(d.Action=="create") { var name=(d.Name??"").Trim();if(name.Length==0)throw new Exception("Enter your name.");name=name[..Math.Min(name.Length,24)];int min=d.MinYear??1950,max=d.MaxYear??DateTime.UtcNow.Year;if(min<1950||max>DateTime.UtcNow.Year||min>max)throw new Exception("Choose a valid year range from 1950 to the current year.");if((long)max-min+1<30)throw new Exception("Choose a range of at least 30 calendar years, including both start and end years.");if(tracks.Count(t=>t[2].GetInt32()>=min&&t[2].GetInt32()<=max)<5)throw new Exception("There are fewer than five songs in that range. Please choose a wider range.");string code;do{code=Token(4)[..6].ToUpperInvariant();}while(store.Rooms.ContainsKey(code));var p=new Player { Id=Token(8), Token=Token(), Name=name };var r=new Room { Code=code,Host=p.Id,Mode=d.Mode=="nearest"?"nearest":"points",MinYear=min,MaxYear=max,Players=[p] };store.Rooms[code]=r;Save();return Results.Json(new { token=p.Token,state=State(r,p) }); }
 if(!store.Rooms.TryGetValue((d.Code??"").ToUpperInvariant(),out var room))throw new Exception("Room not found. Check the code.");
 if(d.Action=="join") { var name=(d.Name??"").Trim();if(name.Length==0)throw new Exception("Enter your name.");if(room.Status!="lobby")throw new Exception("This game has already started.");if(room.Players.Count>=12)throw new Exception("This room is full.");var p=new Player {Id=Token(8),Token=Token(),Name=name[..Math.Min(24,name.Length)]};room.Players.Add(p);Save();return Results.Json(new { token=p.Token,state=State(room,p) }); }
 var player=room.Players.FirstOrDefault(p=>p.Token==d.Token);if(player is null)throw new Exception("Your player session was not found.");
 if(d.Action=="state")return Results.Json(new {state=State(room,player)});
 if(d.Action=="start") {if(player.Id!=room.Host||room.Status!="lobby")throw new Exception("Only the host can start the game.");var deck=Enumerable.Range(0,tracks.Count).Where(i=>tracks[i][2].GetInt32()>=room.MinYear&&tracks[i][2].GetInt32()<=room.MaxYear).ToArray();if(deck.Length<5)throw new Exception("There are fewer than five songs in this range.");for(int i=deck.Length-1;i>0;i--){int j=RandomNumberGenerator.GetInt32(i+1);(deck[i],deck[j])=(deck[j],deck[i]);}room.Deck=deck.Take(5).ToList();room.Round=0;room.Status="playing";room.Guesses.Clear();}
 else if(d.Action=="guess") {if(room.Status!="playing"||room.Guesses.ContainsKey(player.Id))throw new Exception("Your guess is already locked in or the round has ended.");if(d.Year is null||d.Year<1950||d.Year>DateTime.UtcNow.Year)throw new Exception("Enter a valid year.");room.Guesses[player.Id]=d.Year.Value;if(room.Players.All(p=>room.Guesses.ContainsKey(p.Id)))Reveal(room);}
 else if(d.Action=="reveal") {if(player.Id!=room.Host||room.Status!="playing")throw new Exception("Only the host can reveal the answer.");if(!room.Guesses.ContainsKey(player.Id))throw new Exception("Submit your guess first.");Reveal(room);}
 else if(d.Action=="next") {if(player.Id!=room.Host||room.Status!="reveal")throw new Exception("Only the host can continue.");if(room.Round==4){room.Status="finished";foreach(var p in room.Players)store.Scores.Add(new(p.Name,p.Score,room.Mode,DateTime.UtcNow));}else{room.Round++;room.Guesses.Clear();room.Reveal=null;room.Status="playing";}}
 else throw new Exception("Unknown action.");Save();return Results.Json(new {state=State(room,player)});
 } catch(Exception e) { return Results.Json(new {error=e.Message},statusCode:400); } } });
app.Run();
record Command(string? Action,string? Name,string? Mode,string? Code,string? Token,int? Year,int? MinYear,int? MaxYear);
class Store { public Dictionary<string,Room> Rooms {get;set;}=new(); public List<ScoreEntry> Scores {get;set;}=new(); }
class Room { public string Code {get;set;}=""; public string Host {get;set;}=""; public string Mode {get;set;}="points"; public int MinYear {get;set;}=1950; public int MaxYear {get;set;}=2100; public string Status {get;set;}="lobby"; public int Round {get;set;} public List<int> Deck {get;set;}=new(); public List<Player> Players {get;set;}=new(); public Dictionary<string,int> Guesses {get;set;}=new(); public RevealInfo? Reveal {get;set;} }
class Player { public string Id {get;set;}=""; public string Token {get;set;}=""; public string Name {get;set;}=""; public int Score {get;set;} }
record ScoreEntry(string Name,int Score,string Mode,DateTime PlayedAt);
record RoundResult(string Id,string Name,int? Guess,int Points);
record RevealInfo(int Year,List<RoundResult> Results);
