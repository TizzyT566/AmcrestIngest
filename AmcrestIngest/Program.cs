using AmcrestApi;
using static AmcrestApi.Session.FileFindingApi;

const int ERROR_GRACE_TIME = 1;
const int RECHECK_INTERVAL = 5;

if (args.Length < 4)
{
    Console.WriteLine("Missing arguments:\n   0) Save Directory\n   1) Host Address\n   2) Username\n   3) Password");
    return;
}

Mutex singleInstanceMutex = new(false, new Uri(args[1]).ToString());
if (!singleInstanceMutex.WaitOne(0))
{
    Console.WriteLine("Only one instance per host");
    return;
}

Console.Title = $"{args[1]} ingest";

DateTime latestTime = DateTime.MinValue;
Session session = new(args[1], args[2], args[3]);

while (true)
{
    try
    {
        MediaFinder? mediaFinder = await session.FileFinding.CreateMediaFinder();

        if (mediaFinder == null)
        {
            Console.WriteLine($"Unable to initialize MediaFinder, will wait {ERROR_GRACE_TIME}m to try again");
            await Task.Delay((int)TimeSpan.FromMinutes(ERROR_GRACE_TIME).TotalMilliseconds);
            continue;
        }

        DateTime now = DateTime.Now.Subtract(TimeSpan.FromMinutes(1));
        QueryStatus status = await session.FileFinding.SetQuery(mediaFinder, 1, latestTime, now);

        if (status)
        {
            IReadOnlyCollection<QueryItem> items = await session.FileFinding.RunQuery(mediaFinder, 100);
            SortedDictionary<DateTime, QueryItem> uniqueItems = new();

            foreach (QueryItem item in items)
                uniqueItems.TryAdd(item.StartTime, item);

            foreach (KeyValuePair<DateTime, QueryItem> item in uniqueItems)
            {
                if (item.Value.FilePath == null || !item.Value.FilePath.ToLower().EndsWith(".mp4"))
                {
                    Console.WriteLine($"Encountered invalid item, retrying");
                    break;
                }
                await session.DownloadMedia(args[0], item.Value);
                if (item.Value.EndTime >= latestTime)
                    latestTime = item.Value.EndTime.Add(TimeSpan.FromSeconds(1));
            }
        }
        else
        {
            Console.WriteLine($"Upto date, recheck in {RECHECK_INTERVAL}m");
            await Task.Delay((int)TimeSpan.FromMinutes(RECHECK_INTERVAL).TotalMilliseconds);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"General exception: {ex.Message}, will wait {ERROR_GRACE_TIME}m to try again\n{ex.StackTrace}");
        await Task.Delay((int)TimeSpan.FromMinutes(ERROR_GRACE_TIME).TotalMilliseconds);
    }
}