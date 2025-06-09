#ifdef _WIN32
#include "Windows.h"
#define symLoad GetProcAddress
#define symClose FreeLibrary
#else
#include "dlfcn.h"
#define symLoad dlsym
#define symClose dlclose
#endif
#include <iostream>
#include <string>
typedef void (*SetConfigsFunc)(const char* buildConfig, const char* cdnConfig);
typedef void (*SetBaseDirFunc)(const char* wowFolder);
typedef void (*LoadFunc)();
typedef const char* (*GetBuildStringFunc)();
typedef const char* (*GetFileByIDFunc)(const uint32_t fileDataID);
typedef const bool (*FileExistsByIDFunc)(const uint32_t fileDataID);
typedef const uint64_t (*GetFileSizeByIDFunc)(const uint32_t fileDataID);

int main() {
#ifdef _WIN32
	HINSTANCE h = LoadLibrary(L"TACTSharp.Native.dll");
#else
	void* h = dlopen("./TACTSharp.Native.so", RTLD_LAZY);
#endif
	if (!h) {
		std::cerr << "Failed to load TACTSharp.Native library from disk\n";
		return 1;
	}

	SetBaseDirFunc setBaseDir = (SetBaseDirFunc)symLoad(h, "SetBaseDir");
	if (!setBaseDir) {
		std::cerr << "Failed to find SetBaseDir export\n";
		symClose(h);
		return 1;
	}

	SetConfigsFunc setConfigs = (SetConfigsFunc)symLoad(h, "SetConfigs");
	if (!setConfigs) {
		std::cerr << "Failed to find SetConfigs export\n";
		symClose(h);
		return 1;
	}

	LoadFunc load = (LoadFunc)symLoad(h, "Load");
	if (!load) {
		std::cerr << "Failed to find Load export\n";
		symClose(h);
		return 1;
	}

	GetBuildStringFunc getBuildString = (GetBuildStringFunc)symLoad(h, "GetBuildString");
	if (!getBuildString) {
		std::cerr << "Failed to find GetBuildString export\n";
		symClose(h);
		return 1;
	}

	GetFileByIDFunc getFileByID = (GetFileByIDFunc)symLoad(h, "GetFileByID");
	if (!getFileByID) {
		std::cerr << "Failed to find GetFileByID export\n";
		symClose(h);
		return 1;
	}

	FileExistsByIDFunc fileExistsByID = (FileExistsByIDFunc)symLoad(h, "FileExistsByID");
	if (!fileExistsByID) {
		std::cerr << "Failed to find FileExistsByID export\n";
		symClose(h);
		return 1;
	}

	GetFileSizeByIDFunc getFileSizeByID = (GetFileSizeByIDFunc)symLoad(h, "GetFileSizeByID");
	if (!getFileSizeByID) {
		std::cerr << "Failed to find GetFileSizeByID export\n";
		symClose(h);
		return 1;
	}

	setConfigs("43b2762b8e4a57c4771a5cf9a1d99661", "8be9cf988078dd923677d222be5dfe38");
	load();

	std::cout << "Loaded build " << getBuildString() << "\n";

	auto kakapoFDID = 2061670; // Example file data ID

	if(!fileExistsByID(kakapoFDID)) 
	{
		std::cerr << "File with ID " << kakapoFDID << " does not exist\n";
		symClose(h);
		return 1;
	}

	std::cout << "File with ID " << kakapoFDID << " exists\n";

	std::cout << "File size: " << getFileSizeByID(kakapoFDID) << " bytes\n";

	auto filePointer = getFileByID(2061670);
	if(filePointer == 0) 
	{
		std::cerr << "Failed to get file by ID\n";
		symClose(h);
		return 1;
	}

	std::string fourCC(reinterpret_cast<const char*>(filePointer), 4);

	std::cout << "File data: " << fourCC << "\n";
	symClose(h);
	return 0;
}