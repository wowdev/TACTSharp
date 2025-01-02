# TACTSharp
Library utilizing memory-mapped file access to speed up initial loading and have lower RAM usage compared to other libraries. Made so I can extract things on my NAS without it getting resource starved.  
Largely based on @bloerwald's memory-mapped file reading implementations.

# TACTTool
Tool using the library for simple file extraction. 

## Usage
TACTTool `product`:  
- `TACTTool.exe wow` to load retail WoW
- `TACTTool.exe wowt` to load WoW PTR

TACTTool `buildconfig(path)` `cdnconfig(path)`:  
- `TACTTool.exe 0c245919b5294f12f4c65238b15f550c cd18191b8928c33bf24b962e9330460f` to load 11.0.7.58238 (Retail)
- `TACTTool.exe D:\fakebuildconfig D:\fakecdnconfig` to load specific configs from disk

Put files to extract in `extract.txt` in the same folder as the executable with the format `filedataid;filename`.  
Example: To extract BattlePetAbility DB2s put the following in `extract.txt`:

```
801575;DBFilesClient/BattlePetAbilityEffect.db2
801576;DBFilesClient/BattlePetAbilityState.db2
801577;DBFilesClient/BattlePetAbilityTurn.db2
841610;DBFilesClient/BattlePetAbility.db2
```
# Support
Only tested on WoW, other products not supported.
