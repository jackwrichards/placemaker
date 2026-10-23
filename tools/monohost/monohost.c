// Runs a .NET assembly in Cities: Skylines II's own Mono runtime (MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll), with
// the game's Managed folder as its class libraries: the same JIT and garbage collector the mod runs under in the game.
//   tools\monohost\build.bat, then: tools\monohost\bin\monohost.exe <assembly.exe> [args...]
// Used by Tests/Placemaker.OutgrowBench.Mono (the Outgrow planner timed in the game's runtime; see its Program.cs).
#include <windows.h>
#include <stdio.h>

typedef void* (*mono_jit_init_version_t)(const char*, const char*);
typedef void (*mono_set_dirs_t)(const char*, const char*);
typedef void (*mono_config_parse_t)(const char*);
typedef void* (*mono_domain_assembly_open_t)(void*, const char*);
typedef int (*mono_jit_exec_t)(void*, void*, int, char**);
typedef void (*mono_set_assemblies_path_t)(const char*);

#define GAME "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Cities Skylines II"

int main(int argc, char** argv)
{
    if (argc < 2) { fprintf(stderr, "usage: monohost <assembly.exe> [args]\n"); return 2; }
    HMODULE m = LoadLibraryA(GAME "\\MonoBleedingEdge\\EmbedRuntime\\mono-2.0-bdwgc.dll");
    if (!m) { fprintf(stderr, "mono not loaded: %lu\n", GetLastError()); return 3; }
    mono_set_dirs_t set_dirs = (mono_set_dirs_t)GetProcAddress(m, "mono_set_dirs");
    mono_set_assemblies_path_t set_path = (mono_set_assemblies_path_t)GetProcAddress(m, "mono_set_assemblies_path");
    mono_config_parse_t config_parse = (mono_config_parse_t)GetProcAddress(m, "mono_config_parse");
    mono_jit_init_version_t jit_init = (mono_jit_init_version_t)GetProcAddress(m, "mono_jit_init_version");
    mono_domain_assembly_open_t open = (mono_domain_assembly_open_t)GetProcAddress(m, "mono_domain_assembly_open");
    mono_jit_exec_t exec = (mono_jit_exec_t)GetProcAddress(m, "mono_jit_exec");
    if (!set_dirs || !jit_init || !open || !exec) { fprintf(stderr, "mono exports missing\n"); return 4; }
    set_dirs(GAME "\\Cities2_Data\\Managed", GAME "\\MonoBleedingEdge\\etc");
    if (set_path) set_path(GAME "\\Cities2_Data\\Managed");
    if (config_parse) config_parse(NULL);
    void* domain = jit_init("monohost", "v4.0.30319");
    if (!domain) { fprintf(stderr, "no domain\n"); return 5; }
    void* asm_ = open(domain, argv[1]);
    if (!asm_) { fprintf(stderr, "assembly not opened: %s\n", argv[1]); return 6; }
    return exec(domain, asm_, argc - 1, argv + 1);
}
