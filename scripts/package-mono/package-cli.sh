MONOCONFIG=/etc/mono/config
# Update the global DllMap config file to avoid a hard coded location for libMonoPosixHelper
sed -i '/libMonoPosixHelper\.so/c\\	<dllmap dll=\"MonoPosixHelper\" target=\"libMonoPosixHelper\.so\" os=\"!windows\" \/\>' "$MONOCONFIG"


mkbundle -c -o datacli.c -oo datacli.a  \
	EventStore.Data.CLI.exe \
	EventStore.Rags.dll \
	EventStore.Core.dll \
	EventStore.BufferManagement.dll \
	EventStore.Common.dll \
	EventStore.Projections.Core.dll \
	EventStore.Transport.Http.dll \
	EventStore.Transport.Tcp.dll \
	HdrHistogram.NET.dll \
	Newtonsoft.Json.dll \
	NLog.dll protobuf-net.dll \
  Mono.Security.dll \
  System.Net.Http.dll \
	--static --deps --config /etc/mono/config --machine-config /etc/mono/4.0/machine.config

# mkbundle appears to be doing it wrong, though maybe there's something I'm not seeing.
sed -e '/_config_/ s/unsigned //' -i"" datacli.c

# Forcibly set MONO_GC_DEBUG=clear-at-gc unless it's set to something else
# shellcheck disable=SC1004
# (literal linebreak is desired)
sed -e 's/mono_mkbundle_init();/setenv("MONO_GC_DEBUG", "clear-at-gc", 0);\
        mono_mkbundle_init();/' -i"" datacli.c

cc -o data-cli \
    -Wall $(pkg-config --cflags monosgen-2) \
	datacli.c \
    $(pkg-config --libs-only-L monosgen-2) \
	-Wl,-Bstatic -lmonosgen-2.0 \
    -Wl,-Bdynamic $(pkg-config --libs-only-l monosgen-2 | sed -e "s/\-lmonosgen-2.0 //") \
	datacli.a