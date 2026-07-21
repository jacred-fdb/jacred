using JacRed.Infrastructure.Tracks;
using JacRed.Models.Tracks;
using Newtonsoft.Json;
using Xunit;

namespace JacRed.Tests.Tracks;

public class FfprobeModelDeserializeTests
{
    [Fact]
    public void Deserializes_TorrServer_ProbeData_streams_and_tags()
    {
        // Shape matches gopkg.in/vansante/go-ffprobe.v2 ProbeData as returned by GET /ffp/{hash}/{id}
        const string json = """
            {
              "streams": [
                {
                  "index": 0,
                  "codec_name": "h264",
                  "codec_long_name": "H.264 / AVC",
                  "codec_type": "video",
                  "width": 1920,
                  "height": 1080,
                  "bit_rate": "5000000",
                  "tags": { "language": "und", "title": "Video" }
                },
                {
                  "index": 1,
                  "codec_name": "aac",
                  "codec_type": "audio",
                  "sample_rate": "48000",
                  "channels": 2,
                  "channel_layout": "stereo",
                  "bit_rate": "192000",
                  "tags": { "language": "rus", "title": "LostFilm", "BPS": "192000", "DURATION": "01:00:00.000000000" }
                }
              ],
              "format": {
                "format_name": "matroska,webm",
                "duration": "3600.0",
                "size": "1000"
              }
            }
            """;

        var model = JsonConvert.DeserializeObject<FfprobeModel>(json);

        Assert.NotNull(model);
        Assert.NotNull(model.streams);
        Assert.Equal(2, model.streams.Count);

        var video = model.streams[0];
        Assert.Equal("video", video.codec_type);
        Assert.Equal("h264", video.codec_name);
        Assert.Equal(1920, video.width);
        Assert.Equal(1080, video.height);

        var audio = model.streams[1];
        Assert.Equal("audio", audio.codec_type);
        Assert.Equal("aac", audio.codec_name);
        Assert.NotNull(audio.tags);
        Assert.Equal("rus", audio.tags.language);
        Assert.Equal("LostFilm", audio.tags.title);
        Assert.Equal("192000", audio.tags.BPS);
    }

    [Fact]
    public void Ignores_unknown_format_chapters_fields()
    {
        const string json = """
            {
              "streams": [{ "index": 0, "codec_type": "audio", "codec_name": "ac3", "tags": { "language": "eng" } }],
              "chapters": [{ "id": 0, "start_time": "0", "end_time": "10" }],
              "format": { "nb_streams": 1 }
            }
            """;

        var model = JsonConvert.DeserializeObject<FfprobeModel>(json);

        Assert.Single(model.streams);
        Assert.Equal("eng", model.streams[0].tags.language);
    }
}
