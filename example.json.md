```json
{
  "name": "Example Project",
  "tools": {                                                                    //specify where ffmpeg tools are stored
    "ffmpegPath": "/Path/to/ffmpeg",                                            //FFMpeg path
    "ffprobePath": "/Path/to/ffprobe"                                           //FFProbe path
  },
  "output": {               //specify output settings first
    "mode": "separate",     //proccessing mode. valid values: "separate" (process each file seperately) or "concat" (combine into one big file)
    "format": "mp4"         //output format. valid values: "mp4", "webm", "gif"
  },
  "inputs": [                                                                   //specify inputs as an array for each video to process
    {
      "path": "/home/kasey/video1.mp4",                                         //path to input file
      "automaticallyFixOverlappingText": true,                                  //(optional) if true, Video Stamper will automatically fix overlapping text (default true)
      "timestamp": {                                                            //date-time stamp setting
        "enabled": true,                                                        //use timestamp
        "useMetadataCreationTime": true,                                        //use built in metadata time as timestamp base. (unix epoch if false)
        "timeOffset": 0,                                                        //seconds to offset timestamp by
        "format": "yyyy-MM-dd HH:mm:ss",                                        //date time format string as specified here: https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings
        "font": {                                                               //font related settings
          "fontFile": "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",        //path to fontFile
          "size": 32,                                                           //font size
          "color": "white",                                                     //font color
          "borderColor": "black",                                               //border color (optional)
          "borderWidth": 2                                                      //border width in pixels (optional, default 1)
        },
        "position": {                                                           //text position related settings
          "anchor": "bottomRight",                                              //which portion of screen to anchor text to
          "xPad": 5,                                                            //x coordinate offset in percentage from anchor point
          "yPad": 5,                                                            //y coordinate offset in percentage from anchor point
          "xOffset": 16,                                                        //x coordinate offset in pixels from anchor point + padding
          "yOffset": 16                                                         //y coordinate offset in pixels from anchor point + padding
        }
      },
      "subtitles": [                                                            //specify titles as an array for each text to disply
        {
          "text": "Subtitle just for video1",                                   //subtitle text
          "start": 2.0,                                                         //how many seconds into the video does the text start
          "end": 5.0,                                                           //how many seconds into the video does the text end
          "font": {                                                             //font related settings
            "fontFile": "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",      //path to fontfile
            "size": 28,                                                         //font size
            "color": "white",                                                   //font color
            "borderColor": "black",                                             //border color (optional)
            "borderWidth": 2                                                    //border width in pixels (optional, default 1)
          },
          "position": {                                                         //text position related settings
              "anchor": "bottomRight",                                          //which portion of screen to anchor text to
              "xPad": 5,                                                        //x coordinate offset in percentage from anchor point
              "yPad": 5,                                                        //y coordinate offset in percentage from anchor point
              "xOffset": 16,                                                    //x coordinate offset in pixels from anchor point + padding
              "yOffset": 16                                                     //y coordinate offset in pixels from anchor point + padding
          },
          "animationIn": "fade",                                                //text animation in (not implemented)
          "animationOut": "fade",                                               //text animation out (not implemented)
          "animationInDur": "1.0",                                              //text animation in duration in seconds (not implemented)
          "animationOutDur": "1.0"                                              //text animation out duration in seconds (not implemented)
        }
      ]
    }
  ]
}

