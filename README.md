# AmcrestIngest
A program to automatically pull footage off of Amcrest IP cameras.

## Usage
```
AmcrestIngest.exe <SaveDirectory> <HostAddress> <UserName> <Password>
```

Upon succesfully connecting the program will start to retrieve all videos stored on the Amcrest ip cameras (stored on sd card).

It will download all videos and then check in regular 5 minute intervals to see if there are any new videos to download.

The program will skip any videos that it has already downloaded (found in \<SaveDirectory\>) and will not download them again.

In the case of any errors the program will check in regular 1 minute intervals to see if a successful attempt can be made.

### Example
```
AmcrestIngest.exe "C:\Cameras\Front Yard" http://192.168.0.128 admin password123
```

Videos will be saved in the \<SaveDirectory\> with the following structure:
```
SaveDirectory\<Year>\<Month>\<Day>\<Hour>\<File>
```

The folder and its sub-folders will be created if they do not exist.

An example of the directory structure:
```
...\Front Yard\2022\01\24\15 ❨3 PM❩\
```

Files are stored with the following scheme, starting timestamp followed by an arrow bracket then the ending timestamp
```
{yyyy-MM-dd hh∶mm∶ss tt} ❯ {yyyy-MM-dd hh∶mm∶ss tt}.{ext}
```

An example of the a filename:
```
2022-01-24 03∶14∶27 PM ❯ 2022-01-24 03∶14∶50 PM.mp4
```

An example of the full path would be:
```
C:\Cameras\Front Yard\2022\01\24\15 ❨3 PM❩\2022-01-24 03∶14∶27 PM ❯ 2022-01-24 03∶14∶50 PM.mp4
```

## Automation Tip

You can have the program run automatically when the computer turns on using Windows Task Scheduler

Set the "When running the task, use the following user account" to SYSTEM by clicking the "Change User or Group" button.

Check the "Run whether user is logged in or not" option.

Check the "Do not store password. The task will only have access to local computer resources."

Check "Run with highest privileges".

On Triggers click "New..." and set "Begin the task" to "At startup" then click "OK".

On Actions click "New..." and set "Action" to "Start a program".

Click "Browse..." then find and open "AmcrestIngest.exe".

Enter the arguments as you would in a command prompt in the "Add arguments (optional}" field and click "OK".

Uncheck everything in the Conditions tab.

On Settings, Check "Allow task to be run on demand".

Check "Run tasl as soon as possible after a scheduled start is missed".

Check "If the task fails, restart every" and set the time interval as desired. I have it set to 5 minutes, 999 times.

Uncheck "Stop the task if it runs longer than".

Check "If the running task does not end with requested, force it to stop".

Uncheck "If the task is not scheduled to run again, delete it after".

Set "If the task is already running, then the following rule applies" to "Do not start a new instance".
