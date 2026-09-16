#include <stdint.h>

#define LIMINE_COMMON_MAGIC_0 0xc7b1dd30df4c8b88ULL
#define LIMINE_COMMON_MAGIC_1 0x0a82e883a194f07bULL

#define LIMINE_FIRMWARE_TYPE_REQUEST_0 0x8c2f75d90bef28a8ULL
#define LIMINE_FIRMWARE_TYPE_REQUEST_1 0x7045a4688eac00c3ULL

#define LIMINE_FIRMWARE_TYPE_X86BIOS 0ULL
#define LIMINE_FIRMWARE_TYPE_EFI32   1ULL
#define LIMINE_FIRMWARE_TYPE_EFI64   2ULL
#define LIMINE_FIRMWARE_TYPE_SBI     3ULL

struct limine_firmware_type_response
{
    uint64_t revision;
    uint64_t firmware_type;
};

struct limine_firmware_type_request
{
    uint64_t id[4];
    uint64_t revision;
    struct limine_firmware_type_response *response;
};


__attribute__((used, aligned(8)))
static volatile struct limine_firmware_type_request
g_limine_firmware_type_request =
{
    {
        LIMINE_COMMON_MAGIC_0,
        LIMINE_COMMON_MAGIC_1,
        LIMINE_FIRMWARE_TYPE_REQUEST_0,
        LIMINE_FIRMWARE_TYPE_REQUEST_1
    },
    0,
    0
};

#ifdef __cplusplus
extern "C" {
#endif

__attribute__((used)) 
uint64_t cor_get_limine_firmware_type(void)
{
    struct limine_firmware_type_response *response =
        g_limine_firmware_type_request.response;

    if (response == 0)
        return UINT64_MAX;

    return response->firmware_type;
}

#ifdef __cplusplus
}
#endif
