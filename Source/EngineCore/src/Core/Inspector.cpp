#include "Core/Inspector.hpp"
#include "Core/Log.hpp"
#include <algorithm>
#include <format>

namespace VelvX {

static std::vector<Inspector::Field>& registry() {
    static std::vector<Inspector::Field> fields;
    return fields;
}

void Inspector::expose(const std::string& owner, const std::string& name,
                       const std::string& type, const void* address) {
    registry().push_back({owner, name, type, address, nullptr});
}

void Inspector::expose(const std::string& owner, const std::string& name,
                       const std::string& type, const void* address, const void* base) {
    registry().push_back({owner, name, type, address, base});
}

void Inspector::removeFields(const void* base) {
    auto& fields = registry();
    fields.erase(std::remove_if(fields.begin(), fields.end(),
                                [base](const Field& f) { return f.base == base; }),
                 fields.end());
}

const std::vector<Inspector::Field>& Inspector::fields() {
    return registry();
}

void Inspector::dump() {
    Log::info(std::format("Inspector: {} campo(s) UNLOCKED expostos", registry().size()));
    for (const Field& f : registry()) {
        Log::info(std::format("  [inspectable] {}.{} : {} @ {}", f.owner, f.name, f.type, f.address));
    }
}

} // namespace VelvX