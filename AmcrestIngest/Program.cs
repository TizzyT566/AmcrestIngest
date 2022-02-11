using AmcrestApi;
using static AmcrestApi.Session.FileFindingApi;

const int RECHECK_INTERVAL = 1;

if (args.Length < 5)
{
    Console.WriteLine("Missing arguments:\n   0) Save Directory\n   1) Host Address\n   2) Username\n   3) Password\n   4) CombSize (GB)");
    return;
}

int combSize;
if (!int.TryParse(args[4], out combSize))
{
    Console.WriteLine("Invalid CombSize argument. Must be an integer");
    return;
}
combSize *= 1073741824; // Multiply by 1GB

Mutex singleInstanceMutex = new(false, new Uri(args[1]).ToString());
if (!singleInstanceMutex.WaitOne(0))
{
    Console.WriteLine("Only one instance per host");
    return;
}

Console.Title = $"{args[1]} ingest";

Queue<string> getFiles()
{
    string[]? previousFiles = Directory.GetFiles(args[0], "*.*", SearchOption.AllDirectories);
    SortedList<DateTime, string> previousFilesSorted = new();
    foreach (string file in previousFiles)
    {
        string[] parts = Path.GetFileName(file).Split(" ❯ ");
        if (parts.Length == 2 && DateTime.TryParse(parts[0].Replace("∶", ":"), out DateTime fileDateTime))
            previousFilesSorted.Add(fileDateTime, file);
    }
    Queue<string> result = new();
    foreach (KeyValuePair<DateTime, string> file in previousFilesSorted)
        result.Enqueue(file.Value);
    return result;
}

Queue<string> previousFiles = getFiles();

void CombOldFiles()
{
    if (previousFiles.Count == 0)
        previousFiles = getFiles();
    DriveInfo driveInfo = new(args[0][..1]);
    while (driveInfo.AvailableFreeSpace < combSize && previousFiles.TryDequeue(out string? file))
    {
        try
        {
            File.Delete(file);
            Console.WriteLine($"Deleted old footage: {file}");
        }
        catch (Exception) { }
    }
}

DateTime latestTime = DateTime.Now.Subtract(TimeSpan.FromDays(30));
Session session = new(args[1], args[2], args[3]);

while (true)
{
    try
    {
        MediaFinder? mediaFinder = await session.FileFinding.CreateMediaFinder();

        if (mediaFinder == null)
        {
            Console.WriteLine($"Unable to initialize MediaFinder, will wait {RECHECK_INTERVAL}m to try again");
            await Task.Delay((int)TimeSpan.FromMinutes(RECHECK_INTERVAL).TotalMilliseconds);
        }
        else
        {
            DateTime now = DateTime.Now.Subtract(TimeSpan.FromMinutes(1));
            QueryStatus status = await session.FileFinding.SetQuery(mediaFinder, 1, latestTime, now);

            if (status)
            {
                IReadOnlyCollection<QueryItem> items = await session.FileFinding.GetQueryItems(mediaFinder, 100);

                _ = await session.FileFinding.DisposeMediaFinder(mediaFinder);

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
                    CombOldFiles();
                    string? newFile = await session.DownloadMedia(args[0], item.Value);
                    if (newFile != null)
                        previousFiles.Enqueue(newFile);
                    if (item.Value.EndTime >= latestTime)
                        latestTime = item.Value.EndTime.Add(TimeSpan.FromSeconds(1));
                }
            }
            else
            {
                Console.WriteLine($"Upto date, recheck in {RECHECK_INTERVAL}m");
                _ = await session.FileFinding.DisposeMediaFinder(mediaFinder);
                await Task.Delay((int)TimeSpan.FromMinutes(RECHECK_INTERVAL).TotalMilliseconds);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"General exception: {ex.Message}, will wait {RECHECK_INTERVAL}m to try again\n{ex.StackTrace}");
        await Task.Delay((int)TimeSpan.FromMinutes(RECHECK_INTERVAL).TotalMilliseconds);
    }
}