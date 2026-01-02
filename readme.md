```
 __      __ _     _           _____                           
 \ \    / /(_)   | |         /  ___\  _                       
  \ \  / /  _  __| | ___  __ | (___  | |  __ _ _ __ ___  _ __   ___ _ __ 
   \ \/ /  | |/ _  |/ _ \/  \\___  \[   ]/ _' | '_ ' _ \| '_ \ / _ \ '__|
    \  /   | ||(_| || __/|()| ___) | | | |(_| | | | | | | |_) |  __/ |   
     \/    |_|\___.|\___|\__/|_____/ |_| \___.|_| |_| |_| .__/ \___|_|   
                                                        | |              
                                                        |_|
```
# VideoStamper
### Version 0.3

This project is built with dotnet10/c# and is intended to make adding timestamps and subtitles to videos using ffmpeg as simple as possible. Based on my previous PowerShell script ["VideoStamperWin.ps1"](https://github.com/kmstrube81/VideoStamperWin)

Usage:
```
VideoStamper.Cli <project.json>
```
Or use VideoStamper.GUI to import/export project json files and run jobs easily.

For documentation about the proper json structure for a VideoStamper project, refer to ./example.json.md

ffmpeg and ffprobe need to be downloaded and their location specified in the project json or under Advanced Settings in the GUI, they are not distributed as a part of this repo. (Downloader/Updater planned in a future release)

### Features
* Add a timestamp based on built in video metadata (CreationTime)
* Add subtitles at custom times in the video
* Work on multiple videos in a single project
* GUI for easy project management. Import/Export project json files and run them from a single interface
* Concatenate videos together or render separately.
* Import custom fonts.

### Planned Features (not yet implemented)
* Releases for other platforms. (Windows/Linux)
* Text animations
* Concatenating multiple videos together

```
 _____________________                    
/ __      __   _____  \                        
| \ \    / /  /  ___\ |
|  \ \  / /  |  (___  |
|   \ \/ /    \___  \ |
|    \  /     ____) | |
|     \/     |_____/  |
\____________________/
```
