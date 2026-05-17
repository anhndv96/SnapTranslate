using ProtoBuf;
using System.Collections.Generic;

namespace SnapTranslate.Models
{
    [ProtoContract]
    public enum LensOverlayFilterType
    {
        UNKNOWN_FILTER_TYPE = 0,
        TRANSLATE = 2,
        AUTO_FILTER = 7,
    }

    [ProtoContract]
    public enum Platform
    {
        UNSPECIFIED = 0,
        WEB = 3,
    }

    [ProtoContract]
    public enum Surface
    {
        UNSPECIFIED = 0,
        CHROMIUM = 4,
    }

    [ProtoContract]
    public enum CoordinateType
    {
        UNSPECIFIED = 0,
        NORMALIZED = 1,
        IMAGE = 2,
    }

    [ProtoContract]
    public enum PolygonVertexOrdering
    {
        VERTEX_ORDERING_UNSPECIFIED = 0,
        CLOCKWISE = 1,
        COUNTER_CLOCKWISE = 2,
    }

    [ProtoContract]
    public enum WritingDirection
    {
        LEFT_TO_RIGHT = 0,
        RIGHT_TO_LEFT = 1,
        TOP_TO_BOTTOM = 2,
    }

    [ProtoContract]
    public enum TextLayoutWordType
    {
        TEXT = 0,
        FORMULA = 1,
    }

    [ProtoContract]
    public enum PayloadRequestType
    {
        REQUEST_TYPE_DEFAULT = 0,
        REQUEST_TYPE_PDF = 1,
        REQUEST_TYPE_EARLY_PARTIAL_PDF = 3,
        REQUEST_TYPE_WEBPAGE = 2,
    }

    [ProtoContract]
    public enum PayloadCompressionType
    {
        UNCOMPRESSED = 0,
        ZSTD = 1,
    }

    // --------------- Request Id ---------------
    [ProtoContract]
    public class LensOverlayRequestId
    {
        [ProtoMember(1)]
        public ulong Uuid { get; set; }
        [ProtoMember(2)]
        public int SequenceId { get; set; }
        [ProtoMember(3)]
        public int ImageSequenceId { get; set; }
        [ProtoMember(4)]
        public byte[] AnalyticsId { get; set; } = [];
        [ProtoMember(6)]
        public LensOverlayRoutingInfo? RoutingInfo { get; set; }
    }

    [ProtoContract]
    public class LensOverlayRoutingInfo
    {
        [ProtoMember(1)]
        public string ServerAddress { get; set; } = "";
        [ProtoMember(3)]
        public string CellAddress { get; set; } = "";
        [ProtoMember(2)]
        public string BladeTarget { get; set; } = "";
    }

    // --------------- Client Context ---------------
    [ProtoContract]
    public class LensOverlayClientContext
    {
        [ProtoMember(1)]
        public Platform Platform { get; set; } = Platform.WEB;
        [ProtoMember(2)]
        public Surface Surface { get; set; } = Surface.CHROMIUM;
        [ProtoMember(4)]
        public LocaleContext? LocaleContext { get; set; }
        [ProtoMember(6)]
        public string AppId { get; set; } = "";
        [ProtoMember(17)]
        public AppliedFilters? ClientFilters { get; set; }
        [ProtoMember(20)]
        public RenderingContext? RenderingContext { get; set; }
        [ProtoMember(23)]
        public ClientLoggingData? ClientLoggingData { get; set; }
    }

    [ProtoContract]
    public class LocaleContext
    {
        [ProtoMember(1)]
        public string Language { get; set; } = "";
        [ProtoMember(2)]
        public string Region { get; set; } = "";
        [ProtoMember(3)]
        public string TimeZone { get; set; } = "";
    }

    [ProtoContract]
    public class RenderingContext
    {
        [ProtoMember(2)]
        public int RenderingEnvironment { get; set; }
    }

    [ProtoContract]
    public class ClientLoggingData
    {
        [ProtoMember(1)]
        public bool IsHistoryEligible { get; set; }
    }

    // --------------- Filters ---------------
    [ProtoContract]
    public class AppliedFilters
    {
        [ProtoMember(1)]
        public List<AppliedFilter> Filter { get; set; } = [];
    }

    [ProtoContract]
    public class AppliedFilter
    {
        [ProtoMember(1)]
        public LensOverlayFilterType FilterType { get; set; }
    }

    // --------------- Image Data ---------------
    [ProtoContract]
    public class ImageData
    {
        [ProtoMember(1)]
        public ImagePayload? Payload { get; set; }
        [ProtoMember(3)]
        public ImageMetadata? ImageMetadata { get; set; }
        [ProtoMember(4)]
        public List<Geometry> SignificantRegions { get; set; } = [];
    }

    [ProtoContract]
    public class ImagePayload
    {
        [ProtoMember(1)]
        public byte[] ImageBytes { get; set; } = [];
    }

    [ProtoContract]
    public class ImageMetadata
    {
        [ProtoMember(1)]
        public int Width { get; set; }
        [ProtoMember(2)]
        public int Height { get; set; }
    }

    // --------------- Geometry ---------------
    [ProtoContract]
    public class Geometry
    {
        [ProtoMember(1)]
        public CenterRotatedBox? BoundingBox { get; set; }
        [ProtoMember(5)]
        public List<Polygon> SegmentationPolygon { get; set; } = [];
    }

    [ProtoContract]
    public class CenterRotatedBox
    {
        [ProtoMember(1)]
        public float CenterX { get; set; }
        [ProtoMember(2)]
        public float CenterY { get; set; }
        [ProtoMember(3)]
        public float Width { get; set; }
        [ProtoMember(4)]
        public float Height { get; set; }
        [ProtoMember(5)]
        public float RotationZ { get; set; }
        [ProtoMember(6)]
        public CoordinateType CoordinateType { get; set; }
    }

    [ProtoContract]
    public class Polygon
    {
        [ProtoMember(1)]
        public List<PolygonVertex> Vertex { get; set; } = [];
        [ProtoMember(2)]
        public PolygonVertexOrdering VertexOrdering { get; set; }
        [ProtoMember(3)]
        public CoordinateType CoordinateType { get; set; }
    }

    [ProtoContract]
    public class PolygonVertex
    {
        [ProtoMember(1)]
        public float X { get; set; }
        [ProtoMember(2)]
        public float Y { get; set; }
    }

    // --------------- Text Layout ---------------
    [ProtoContract]
    public class Text
    {
        [ProtoMember(1)]
        public TextLayout? TextLayout { get; set; }
        [ProtoMember(2)]
        public string ContentLanguage { get; set; } = "";
    }

    [ProtoContract]
    public class TextLayout
    {
        [ProtoMember(1)]
        public List<TextLayoutParagraph> Paragraphs { get; set; } = [];
    }

    [ProtoContract]
    public class TextEntityIdentifier
    {
        [ProtoMember(1)]
        public long Id { get; set; }
    }

    [ProtoContract]
    public class TextLayoutParagraph
    {
        [ProtoMember(1)]
        public TextEntityIdentifier? Id { get; set; }
        [ProtoMember(2)]
        public List<TextLayoutLine> Lines { get; set; } = [];
        [ProtoMember(3)]
        public Geometry? Geometry { get; set; }
        [ProtoMember(4)]
        public WritingDirection WritingDirection { get; set; }
        [ProtoMember(5)]
        public string ContentLanguage { get; set; } = "";
    }

    [ProtoContract]
    public class TextLayoutLine
    {
        [ProtoMember(1)]
        public List<TextLayoutWord> Words { get; set; } = [];
        [ProtoMember(2)]
        public Geometry? Geometry { get; set; }
    }

    [ProtoContract]
    public class TextLayoutWord
    {
        [ProtoMember(1)]
        public TextEntityIdentifier? Id { get; set; }
        [ProtoMember(2)]
        public string PlainText { get; set; } = "";
        [ProtoMember(3)]
        public string TextSeparator { get; set; } = "";
        [ProtoMember(4)]
        public Geometry? Geometry { get; set; }
        [ProtoMember(5)]
        public TextLayoutWordType Type { get; set; }
    }

    // --------------- Request & Response ---------------
    [ProtoContract]
    public class LensOverlayServerRequest
    {
        [ProtoMember(1)]
        public LensOverlayObjectsRequest? ObjectsRequest { get; set; }
        [ProtoMember(2)]
        public LensOverlayInteractionRequest? InteractionRequest { get; set; }
        [ProtoMember(3)]
        public LensOverlayClientLogs? ClientLogs { get; set; }
    }

    [ProtoContract]
    public class LensOverlayObjectsRequest
    {
        [ProtoMember(1)]
        public LensOverlayRequestContext? RequestContext { get; set; }
        [ProtoMember(3)]
        public ImageData? ImageData { get; set; }
        [ProtoMember(4)]
        public Payload? Payload { get; set; }
    }

    [ProtoContract]
    public class LensOverlayRequestContext
    {
        [ProtoMember(3)]
        public LensOverlayRequestId? RequestId { get; set; }
        [ProtoMember(4)]
        public LensOverlayClientContext? ClientContext { get; set; }
    }

    [ProtoContract]
    public class LensOverlayServerResponse
    {
        [ProtoMember(1)]
        public LensOverlayServerError? Error { get; set; }
        [ProtoMember(2)]
        public LensOverlayObjectsResponse? ObjectsResponse { get; set; }
        [ProtoMember(3)]
        public LensOverlayInteractionResponse? InteractionResponse { get; set; }
    }

    [ProtoContract]
    public class LensOverlayObjectsResponse
    {
        [ProtoMember(2)]
        public List<OverlayObject> OverlayObjects { get; set; } = [];
        [ProtoMember(3)]
        public Text? Text { get; set; }
        [ProtoMember(4)]
        public List<DeepGleamData> DeepGleams { get; set; } = [];
        [ProtoMember(7)]
        public LensOverlayClusterInfo? ClusterInfo { get; set; }
    }

    [ProtoContract]
    public class OverlayObject
    {
        [ProtoMember(1)]
        public string Id { get; set; } = "";
        [ProtoMember(2)]
        public Geometry? Geometry { get; set; }
    }

    [ProtoContract]
    public class DeepGleamData
    {
        [ProtoMember(10)]
        public TranslationData? Translation { get; set; }
        [ProtoMember(11)]
        public List<string> VisualObjectId { get; set; } = [];
    }

    [ProtoContract]
    public class TranslationData
    {
        [ProtoMember(1)]
        public TranslationDataStatus? Status { get; set; }
        [ProtoMember(2)]
        public string TargetLanguage { get; set; } = "";
        [ProtoMember(3)]
        public string SourceLanguage { get; set; } = "";
        [ProtoMember(4)]
        public string Translation { get; set; } = "";
    }

    [ProtoContract]
    public class TranslationDataStatus
    {
        [ProtoMember(1)]
        public int Code { get; set; }
    }

    [ProtoContract]
    public class LensOverlayClusterInfo
    {
        [ProtoMember(1)]
        public string ServerSessionId { get; set; } = "";
        [ProtoMember(2)]
        public string SearchSessionId { get; set; } = "";
        [ProtoMember(6)]
        public LensOverlayRoutingInfo? RoutingInfo { get; set; }
    }

    [ProtoContract]
    public class LensOverlayServerError
    {
        [ProtoMember(1)]
        public int ErrorType { get; set; }
    }

    [ProtoContract]
    public class Payload
    {
        [ProtoMember(6)]
        public PayloadRequestType RequestType { get; set; }
        [ProtoMember(2)]
        public ImageData? ImageData { get; set; }
        [ProtoMember(3)]
        public byte[] ContentData { get; set; } = [];
        [ProtoMember(4)]
        public string ContentType { get; set; } = "";
        [ProtoMember(5)]
        public string PageUrl { get; set; } = "";
        [ProtoMember(8)]
        public PayloadCompressionType CompressionType { get; set; }
    }

    [ProtoContract]
    public class LensOverlayInteractionRequest { }

    [ProtoContract]
    public class LensOverlayInteractionResponse { }

    [ProtoContract]
    public class LensOverlayClientLogs { }
}