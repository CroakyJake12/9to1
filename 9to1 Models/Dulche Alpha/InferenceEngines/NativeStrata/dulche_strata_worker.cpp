#include "dulche_strata.h"
#include <bit>
#include <cstdio>
#include <cstring>
#include <limits>
#include <memory>
#include <new>
#include <stdexcept>
#include <string>
#include <type_traits>
#include <vector>
#include <unistd.h>
#if STRATA_HAS_CUDA
#include <cuda_runtime.h>
#endif

/* Dedicated bundled worker: crashes/OOM/forced cancellation cannot destroy the Dulche host.
   stdin/stdout carry only bounded little-endian frames; native diagnostic stdout goes to stderr. */
namespace {
constexpr size_t limit = 16U * 1024U * 1024U;
struct Buffer {
    std::vector<unsigned char> bytes; size_t cursor{};
    template<class T> void number(T value) {
        static_assert(std::is_unsigned_v<T>);
        for (size_t index = 0; index < sizeof(T); ++index) bytes.push_back(static_cast<unsigned char>(value >> (index * 8U)));
    }
    template<class T> T read_number() {
        static_assert(std::is_unsigned_v<T>);
        if (bytes.size() - cursor < sizeof(T)) throw std::invalid_argument("Truncated native command.");
        T value{}; for (size_t index = 0; index < sizeof(T); ++index) value |= static_cast<T>(bytes[cursor++]) << (index * 8U); return value;
    }
    void text(dulche_strata_text value) {
        if (value.size > limit || value.size > std::numeric_limits<uint32_t>::max()) throw std::invalid_argument("Oversized native output.");
        number(static_cast<uint32_t>(value.size));
        if (value.size != 0) bytes.insert(bytes.end(), value.data, value.data + value.size);
    }
    void text(const std::string& value) { text(dulche_strata_text{value.data(),value.size()}); }
    std::string read_text() {
        const auto size = read_number<uint32_t>();
        if (size > limit || size > bytes.size() - cursor) throw std::invalid_argument("Invalid native text length.");
        std::string value(reinterpret_cast<const char*>(bytes.data() + cursor),size); cursor += size; return value;
    }
    void real(double value) { number(std::bit_cast<uint64_t>(value)); }
    double read_real() { return std::bit_cast<double>(read_number<uint64_t>()); }
    void end() const { if (cursor != bytes.size()) throw std::invalid_argument("Unexpected native command suffix."); }
};
void write(FILE* wire, uint32_t type, const Buffer& payload) {
    if (payload.bytes.size() > limit) throw std::invalid_argument("Native output frame exceeds limit.");
    Buffer header; header.number(type); header.number(static_cast<uint32_t>(payload.bytes.size()));
    if (std::fwrite(header.bytes.data(),1,header.bytes.size(),wire) != header.bytes.size()
        || (!payload.bytes.empty() && std::fwrite(payload.bytes.data(),1,payload.bytes.size(),wire) != payload.bytes.size())
        || std::fflush(wire) != 0) throw std::runtime_error("Native output pipe failed.");
}
bool read(Buffer& result) {
    unsigned char length[4]; const auto count = std::fread(length,1,4,stdin);
    if (count == 0 && std::feof(stdin)) return false;
    if (count != 4) throw std::invalid_argument("Truncated native command length.");
    uint32_t size{}; for (unsigned index=0;index<4;++index) size |= static_cast<uint32_t>(length[index]) << (index*8U);
    if (size > limit || size < 4) throw std::invalid_argument("Invalid bounded native command length.");
    result.bytes.resize(size); result.cursor=0;
    if (std::fread(result.bytes.data(),1,size,stdin) != size) throw std::invalid_argument("Truncated native command frame.");
    return true;
}
/* Fixed no-model probe. The sole admitted argument bypasses RuntimeSession construction,
   loads no checkpoint, accepts no stdin model commands and naturally exits after its receipt. */
Buffer hardware_probe() {
    Buffer output;
#if STRATA_HAS_CUDA
    const auto require = [](cudaError_t status, const char* operation) {
        if (status != cudaSuccess) throw std::runtime_error(std::string(operation) + ": " + cudaGetErrorString(status));
    };
    int runtime{}, driver{}, count{};
    require(cudaRuntimeGetVersion(&runtime), "cudaRuntimeGetVersion");
    require(cudaDriverGetVersion(&driver), "cudaDriverGetVersion");
    require(cudaGetDeviceCount(&count), "cudaGetDeviceCount");
    if (runtime <= 0 || driver <= 0 || count < 0 || count > 64) throw std::runtime_error("Invalid bounded actual CUDA inventory.");
    output.number<uint32_t>(0); output.number<uint32_t>(0);
    output.number(static_cast<uint32_t>(runtime)); output.number(static_cast<uint32_t>(driver)); output.number(static_cast<uint32_t>(count));
    for (int index = 0; index < count; ++index) {
        cudaDeviceProp properties{}; require(cudaGetDeviceProperties(&properties,index), "cudaGetDeviceProperties");
        require(cudaSetDevice(index), "cudaSetDevice");
        size_t free{}, total{}; require(cudaMemGetInfo(&free,&total), "cudaMemGetInfo");
        if (properties.major < 0 || properties.minor < 0 || total == 0 || free > total || properties.totalGlobalMem == 0)
            throw std::runtime_error("Invalid actual CUDA device properties/memory.");
        std::string uuid; uuid.reserve(32); constexpr char hex[] = "0123456789abcdef";
        for (const auto byte : properties.uuid.bytes) {
            const auto value = static_cast<unsigned char>(byte);
            uuid.push_back(hex[value >> 4U]); uuid.push_back(hex[value & 15U]);
        }
        output.number(static_cast<uint32_t>(index));
        output.text(std::string(properties.name, ::strnlen(properties.name,sizeof(properties.name)))); output.text(uuid);
        output.number(static_cast<uint32_t>(properties.major)); output.number(static_cast<uint32_t>(properties.minor));
        output.number(static_cast<uint64_t>(properties.totalGlobalMem)); output.number(static_cast<uint64_t>(free)); output.number(static_cast<uint64_t>(total));
    }
    return output;
#else
    output.number<uint32_t>(1); output.number<uint32_t>(1); output.text(std::string("This worker has no actual CUDA build.")); return output;
#endif
}
using Result = std::unique_ptr<dulche_strata_result,decltype(&dulche_strata_free_result)>;
void errors(Buffer& output, const dulche_strata_result* result) {
    output.number(static_cast<uint32_t>(dulche_strata_result_code(result)));
    output.number(static_cast<uint32_t>(dulche_strata_error_count(result)));
    for (size_t index=0;index<dulche_strata_error_count(result);++index) output.text(dulche_strata_error(result,index));
}
dulche_strata_text span(const std::string& value) { return {value.data(),value.size()}; }
struct Part { uint32_t kind{}; std::string data,mime; };
struct Message { uint32_t role{}; std::string text,name; std::vector<Part> parts; std::vector<dulche_strata_part> raw_parts; };
void generate(dulche_strata_session* session, Buffer& input, FILE* wire) {
    const auto count=input.read_number<uint32_t>(); if (count>4096) throw std::invalid_argument("Too many native chat messages.");
    std::vector<Message> messages; messages.reserve(count);
    for (uint32_t index=0;index<count;++index) {
        Message message; message.role=input.read_number<uint32_t>(); message.text=input.read_text(); message.name=input.read_text();
        const auto parts=input.read_number<uint32_t>(); if (parts>128) throw std::invalid_argument("Too many native input parts.");
        for (uint32_t part=0;part<parts;++part) message.parts.push_back({input.read_number<uint32_t>(),input.read_text(),input.read_text()});
        messages.push_back(std::move(message));
    }
    dulche_strata_options options{}; options.maximum_new_tokens=input.read_number<uint32_t>();
    if (options.maximum_new_tokens==0 || options.maximum_new_tokens>4096) throw std::invalid_argument("Native output token limit is invalid.");
    options.temperature=input.read_real(); options.top_p=input.read_real(); options.top_k=input.read_number<uint32_t>(); options.seed=input.read_number<uint64_t>();
    const auto stop_count=input.read_number<uint32_t>(); if (stop_count>128) throw std::invalid_argument("Too many native stop sequences.");
    std::vector<std::string> stops; for(uint32_t index=0;index<stop_count;++index) stops.push_back(input.read_text());
    std::vector<dulche_strata_text> raw_stops; for(const auto& stop:stops) raw_stops.push_back(span(stop));
    options.stops=raw_stops.data(); options.stop_count=raw_stops.size(); const auto reasoning=input.read_text(); options.reasoning=span(reasoning); input.end();
    std::vector<dulche_strata_message> raw_messages;
    for(auto& message:messages) {
        for(const auto& part:message.parts) message.raw_parts.push_back({part.kind,span(part.data),span(part.mime)});
        raw_messages.push_back({message.role,span(message.text),span(message.name),message.raw_parts.data(),message.raw_parts.size()});
    }
    Result result(dulche_strata_generate(session,raw_messages.data(),raw_messages.size(),&options,
        [](uint32_t token,const char* value,size_t size,void* state) {
            Buffer delta; delta.number(token); delta.text(dulche_strata_text{value,size}); write(static_cast<FILE*>(state),1,delta); return 1;
        },wire),dulche_strata_free_result);
    Buffer terminal; errors(terminal,result.get()); terminal.text(dulche_strata_result_text(result.get()));
    terminal.number(dulche_strata_prompt_tokens(result.get())); terminal.number(dulche_strata_prefill_tokens(result.get())); terminal.number(dulche_strata_decode_tokens(result.get()));
    terminal.real(dulche_strata_prefill_seconds(result.get())); terminal.real(dulche_strata_decode_seconds(result.get()));
    uint64_t reused{}; const auto has_reused=dulche_strata_reused_tokens(result.get(),&reused); terminal.number(static_cast<uint32_t>(has_reused)); if(has_reused) terminal.number(reused);
    int incremental{}; const auto has_incremental=dulche_strata_incremental_kv(result.get(),&incremental); terminal.number(static_cast<uint32_t>(has_incremental)); if(has_incremental) terminal.number(static_cast<uint32_t>(incremental));
    terminal.number(static_cast<uint32_t>(dulche_strata_stopped(result.get()))); write(wire,2,terminal);
}
}
int main(int argc, char** argv) {
    const bool probe = argc == 2 && std::strcmp(argv[1],"--hardware-probe") == 0;
    if (argc != 1 && !probe) return 64;
    FILE* wire=::fdopen(::dup(STDOUT_FILENO),"wb"); if(wire==nullptr || ::dup2(STDERR_FILENO,STDOUT_FILENO)<0) return 70;
    std::unique_ptr<dulche_strata_session,decltype(&dulche_strata_destroy)> session(nullptr,dulche_strata_destroy);
    if(!probe) { session.reset(dulche_strata_create()); if(!session) return 71; }
    try {
        Buffer hello; hello.number(dulche_strata_abi_version()); hello.text(std::string(dulche_strata_origin_commit()));
        hello.number(static_cast<uint32_t>(dulche_strata_has_cuda())); hello.number(static_cast<uint32_t>(dulche_strata_has_nccl()));
        const auto* registry=dulche_strata_registered_models(); if(registry==nullptr) throw std::bad_alloc();
        hello.text(std::string(registry)); write(wire,0,hello);
        if(probe) {
            try {
                const auto observed = hardware_probe(); write(wire,6,observed); std::fclose(wire);
                return dulche_strata_has_cuda() ? 0 : 69;
            }
            catch(const std::exception& error) {
                Buffer failure; failure.number<uint32_t>(3); failure.number<uint32_t>(1); failure.text(std::string(error.what()));
                write(wire,6,failure); std::fclose(wire); return 69;
            }
        }
        Buffer command;
        while(read(command)) {
            const auto operation=command.read_number<uint32_t>();
            if(operation==1) {
                const auto directory=command.read_text(), registration=command.read_text(); const auto context=command.read_number<uint32_t>();
                const auto count=command.read_number<uint32_t>(); if(count>64) throw std::invalid_argument("Too many native devices.");
                std::vector<int> devices; for(uint32_t index=0;index<count;++index) {
                    const auto id=command.read_number<uint32_t>(); if(id>static_cast<uint32_t>(std::numeric_limits<int>::max())) throw std::invalid_argument("Invalid CUDA device index.");
                    devices.push_back(static_cast<int>(id));
                }
                command.end(); Result result(dulche_strata_load(session.get(),span(directory),span(registration),context,devices.data(),devices.size()),dulche_strata_free_result);
                Buffer loaded; errors(loaded,result.get()); write(wire,4,loaded);
            }
            else if(operation==2) generate(session.get(),command,wire);
            else if(operation==3) { command.end(); session.reset(); Buffer stopped; write(wire,3,stopped); std::fclose(wire); return 0; }
            else throw std::invalid_argument("Unsupported native operation.");
        }
        session.reset(); std::fclose(wire); return 0;
    }
    catch(const std::exception& error) {
        try { Buffer failure; failure.number<uint32_t>(3); failure.number<uint32_t>(1); failure.text(std::string(error.what())); write(wire,5,failure); } catch(...) {}
        session.reset(); std::fclose(wire); return 72;
    }
    catch(...) { session.reset(); std::fclose(wire); return 73; }
}
