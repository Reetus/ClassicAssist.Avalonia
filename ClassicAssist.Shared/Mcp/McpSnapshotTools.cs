using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClassicAssist.Data.Screenshot;
using ClassicAssist.Plugin.Shared;
using ClassicAssist.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Mcp;

/// <summary>
///     The MCP snapshot tool. Upstream captured the window with GDI; this fork reads the frame the
///     client just drew (see <see cref="ReflectionCommands.CaptureClientFrame" />) and encodes it
///     through the Avalonia screenshot composer. That means there is no desktop/fullscreen capture -
///     only the client window - and the pixels never touch disk beyond the handover frame file.
/// </summary>
public static class McpSnapshotTools
{
    public static IReadOnlyList<McpTool> GetTools()
    {
        return
        [
            new()
            {
                Name = "getSnapshot",
                Description =
                    "Capture the game client's last drawn frame and return it as an image the model can see, plus basic " +
                    "metadata. The capture is kept in memory and returned as base64 - no file is written. Only the client " +
                    "window is captured; the desktop cannot be captured.",
                InputSchema = McpTools.ObjectSchema(
                    new JObject
                    {
                        ["maxWidth"] = McpTools.IntegerProperty(
                            "Optional maximum image width; wider captures are downscaled (default 0 = native)." ),
                        ["format"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray( "png", "jpeg" ),
                            ["description"] = "Image format: png (default) or jpeg (smaller)."
                        }
                    } )
            }
        ];
    }

    public static CallToolResult Invoke( string name, JObject args )
    {
        try
        {
            switch ( name )
            {
                case "getSnapshot":
                    return GetSnapshot( McpTools.GetInt( args, "maxWidth" ), McpTools.GetString( args, "format" ) )
                        .GetAwaiter().GetResult();
                default:
                    return null;
            }
        }
        catch ( Exception e )
        {
            return McpTools.Error( e.Message );
        }
    }

    private static async Task<CallToolResult> GetSnapshot( int? maxWidth, string format )
    {
        ScreenshotFrame frame = await ReflectionCommands.CaptureClientFrame();

        if ( frame?.Path == null )
        {
            throw new InvalidOperationException(
                "The client could not be captured - it is either unsupported or not currently ticking." );
        }

        try
        {
            if ( Engine.ScreenshotComposer == null )
            {
                throw new InvalidOperationException(
                    "No screenshot composer is available - this host cannot render images." );
            }

            ScreenshotImage image = await Engine.ScreenshotComposer.EncodeAsync( new ScreenshotEncodeRequest
            {
                FramePath = frame.Path,
                Width = frame.Width,
                Height = frame.Height,
                MaxWidth = maxWidth,
                Format = format
            } );

            return new CallToolResult
            {
                Content =
                [
                    new McpContent
                    {
                        Type = "image", Data = Convert.ToBase64String( image.Data ), MimeType = image.MimeType
                    },
                    new McpContent
                    {
                        Type = "text",
                        Text = new JObject
                        {
                            ["width"] = image.Width,
                            ["height"] = image.Height,
                            ["format"] = image.Format,
                            ["capturedAt"] = DateTime.Now.ToString( "o" ),
                            ["byteLength"] = image.Data.Length
                        }.ToString( Formatting.Indented )
                    }
                ]
            };
        }
        finally
        {
            // The frame file is ours once it has been read - see ScreenshotFrame.
            try
            {
                File.Delete( frame.Path );
            }
            catch
            {
                // Swept by the plugin later if it is still there.
            }
        }
    }
}
