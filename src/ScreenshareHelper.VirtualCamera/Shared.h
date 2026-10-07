#pragma once
#include <windows.h>
#include <cstdint>

// COM class of the media source the Windows camera service (Frame Server) loads.
// {D99C4FF7-48EE-4763-99DE-AAA8BE60A97E}
static const GUID CLSID_ScreenshareHelperVirtualCamera =
    { 0xd99c4ff7, 0x48ee, 0x4763, { 0x99, 0xde, 0xaa, 0xa8, 0xbe, 0x60, 0xa9, 0x7e } };
#define VCAM_CLSID_STRING L"{D99C4FF7-48EE-4763-99DE-AAA8BE60A97E}"

// Frames are handed over from the app (user session) to the media source (camera service, session 0)
// through shared memory in the global namespace. The media source creates it, the app opens it.
// Layout must match VirtualCameraOutput.cs.
#define VCAM_SHARED_MEMORY_NAME L"Global\\ScreenshareHelperVirtualCamera"

constexpr uint32_t VCAM_WIDTH = 1920;
constexpr uint32_t VCAM_HEIGHT = 1080;
constexpr uint32_t VCAM_FPS = 30;
constexpr uint32_t VCAM_MAGIC = 0x43564853; // "SHVC"

struct VCamHeader
{
    uint32_t magic;
    uint32_t width;
    uint32_t height;
    volatile LONG latestSlot;      // slot with the newest complete frame, -1 = none yet
    volatile LONG64 frameCounter;  // incremented by the app per frame
    uint8_t reserved[40];
};
static_assert(sizeof(VCamHeader) == 64, "header size is part of the shared layout");

constexpr size_t VCAM_SLOT_SIZE = size_t(VCAM_WIDTH) * VCAM_HEIGHT * 4; // BGRA, top-down
constexpr size_t VCAM_SHARED_SIZE = sizeof(VCamHeader) + 2 * VCAM_SLOT_SIZE;
