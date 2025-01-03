# TACTSharp
Library utilizing memory-mapped file access to speed up initial loading and have lower RAM usage compared to other libraries. Made so I can extract things on my NAS without it getting resource starved.  
Largely based on @bloerwald's memory-mapped file reading implementations.

# TACTTool
Tool using the library for simple file extraction. 

## Usage
```
Description:
  TACTTool - Extraction tool using the TACTSharp library

Usage:
  TACTTool [options]

Options:
  -b, --buildconfig <buildconfig>  Build config to load (hex or file on disk)
  -c, --cdnconfig <cdnconfig>      CDN config to load (hex or file on disk)
  -s, --source <source>            Data source: online or local [default: online]
  -p, --product <product>          TACT product to load [default: wow]
  -r, --region <region>            Region to use for patch service/build selection/CDNs [default: us]
  -o, --output <output>            Output directory for extracted files [default: output]
  --basedir <basedir>              WoW installation folder (if available) (NYI)
  --version                        Show version information
  -?, -h, --help                   Show help and usage information
```
### Examples
- `TACTTool --product wow` to load retail WoW
- `TACTTool --buildconfig 0c245919b5294f12f4c65238b15f550c --cdnconfig cd18191b8928c33bf24b962e9330460f` to load 11.0.7.58238 (Retail)
- `TACTTool -b D:\fakebuildconfig -c D:\fakecdnconfig` to load specific configs from disk (with alias arguments for buildconfig/cdnconfig)

### Extraction
Put files to extract in `extract.txt` in the same folder as the executable with the format `filedataid;filename`.  
> Note: If you want to extract a file from the `install` manifest, either add a line with e.g. `0;WoW.exe` or just `WoW.exe`. Make sure the name matches the one in the `install` manifest exactly.  

Example: To extract BattlePetAbility DB2s put the following in `extract.txt`:

```
801575;DBFilesClient/BattlePetAbilityEffect.db2
801576;DBFilesClient/BattlePetAbilityState.db2
801577;DBFilesClient/BattlePetAbilityTurn.db2
841610;DBFilesClient/BattlePetAbility.db2
```
# Support
Only tested on WoW, other products not supported.
