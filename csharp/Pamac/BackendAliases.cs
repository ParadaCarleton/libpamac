namespace Pamac;

// Compatibility spellings matching the names of the original dynamically
// loaded Vala modules. New code can use the *Client and I*Plugin names.
public interface AURPlugin : IAURPlugin { }
public interface AppstreamPlugin : IAppstreamPlugin { }
public interface SnapPlugin : ISnapPlugin { }
public interface FlatpakPlugin : IFlatpakPlugin { }

public class AUR : AURClient, AURPlugin { }
public class Appstream : AppstreamClient, AppstreamPlugin { }
public class Snap : SnapClient, SnapPlugin { }
public class FlatPak : FlatpakClient, FlatpakPlugin { }
