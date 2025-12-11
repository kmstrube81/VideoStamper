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
### Version 0.1

This project is built with dotnet10/c# and is intended to make adding timestamps and subtitles to videos using ffmpeg as simple as possible. Based on my previous PowerShell script ["VideoStamperWin.ps1"](https://github.com/kmstrube81/VideoStamperWin)

Usage:
```
VideoStamper.Cli <project.json>
```

For documentation about the proper json structure for a VideoStamper project, refer to ./example.json.md

ffmpeg and ffprobe need to be stored in bin/macOS-arm64, they are not distributed as a part of this repo. (Downloader/Updater planned in a future release)

### Features
* Add a timestamp based on built in video metadata (CreationTime)
* Add subtitles at custom times in the video
* Work on multiple videos in a single project

### Planned Features (not yet implemented)
* Releases for other platforms. (Windows/Linux)
* GUI for easily managing projects
* Text animations
* Concatenating multiple videos together
