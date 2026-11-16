#include "Utils/Json.hpp"
#include <cctype>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <sstream>
#include <stdexcept>

namespace VelvX::Json {

Value& Value::operator[](const std::string& key) {
    type = Type::Object;
    for (auto& kv : obj) {
        if (kv.first == key) return kv.second;
    }
    obj.emplace_back(key, Value{});
    return obj.back().second;
}

const Value& Value::operator[](const std::string& key) const {
    if (const Value* v = find(key)) return *v;
    static const Value nullValue;
    return nullValue;
}

const Value* Value::find(const std::string& key) const {
    for (const auto& kv : obj) {
        if (kv.first == key) return &kv.second;
    }
    return nullptr;
}

std::string Value::asString(const std::string& def) const {
    return type == Type::String ? str : def;
}

double Value::asNumber(double def) const {
    return type == Type::Number ? number : def;
}

bool Value::asBool(bool def) const {
    return type == Type::Bool ? boolean : def;
}

static void dumpString(std::ostream& os, const std::string& s) {
    os << '"';
    for (char c : s) {
        switch (c) {
            case '"': os << "\\\""; break;
            case '\\': os << "\\\\"; break;
            case '\n': os << "\\n"; break;
            case '\r': os << "\\r"; break;
            case '\t': os << "\\t"; break;
            default:
                if (static_cast<unsigned char>(c) < 0x20) {
                    char buf[8];
                    std::snprintf(buf, sizeof(buf), "\\u%04x", c);
                    os << buf;
                } else {
                    os << c;
                }
        }
    }
    os << '"';
}

static void dumpValue(std::ostream& os, const Value& v, int indent, int depth) {
    const std::string pad(static_cast<size_t>(indent) * (depth + 1), ' ');
    const std::string padEnd(static_cast<size_t>(indent) * depth, ' ');
    switch (v.type) {
        case Value::Type::Null: os << "null"; break;
        case Value::Type::Bool: os << (v.boolean ? "true" : "false"); break;
        case Value::Type::Number: {
            if (std::floor(v.number) == v.number && std::abs(v.number) < 1e15) {
                os << static_cast<long long>(v.number);
            } else {
                os << v.number;
            }
            break;
        }
        case Value::Type::String: dumpString(os, v.str); break;
        case Value::Type::Array: {
            if (v.arr.empty()) { os << "[]"; break; }
            os << "[\n";
            for (size_t i = 0; i < v.arr.size(); ++i) {
                os << pad;
                dumpValue(os, v.arr[i], indent, depth + 1);
                if (i + 1 < v.arr.size()) os << ',';
                os << '\n';
            }
            os << padEnd << ']';
            break;
        }
        case Value::Type::Object: {
            if (v.obj.empty()) { os << "{}"; break; }
            os << "{\n";
            for (size_t i = 0; i < v.obj.size(); ++i) {
                os << pad;
                dumpString(os, v.obj[i].first);
                os << ": ";
                dumpValue(os, v.obj[i].second, indent, depth + 1);
                if (i + 1 < v.obj.size()) os << ',';
                os << '\n';
            }
            os << padEnd << '}';
            break;
        }
    }
}

std::string Value::dump(int indent) const {
    std::ostringstream os;
    dumpValue(os, *this, indent, 0);
    return os.str();
}

// ---------------------------------------------------------------------------
// Parser
// ---------------------------------------------------------------------------

namespace {

struct Parser {
    const std::string& s;
    size_t i = 0;

    explicit Parser(const std::string& text) : s(text) {}

    [[noreturn]] void fail(const std::string& msg) {
        throw std::runtime_error("JSON: " + msg + " (posição " + std::to_string(i) + ")");
    }

    void ws() {
        while (i < s.size() && std::isspace(static_cast<unsigned char>(s[i]))) ++i;
    }

    char peek() {
        ws();
        if (i >= s.size()) fail("fim inesperado");
        return s[i];
    }

    bool consume(const char* lit) {
        ws();
        const size_t n = std::strlen(lit);
        if (s.compare(i, n, lit) == 0) { i += n; return true; }
        return false;
    }

    std::string parseString() {
        if (peek() != '"') fail("esperada string");
        ++i;
        std::string out;
        while (i < s.size() && s[i] != '"') {
            char c = s[i++];
            if (c == '\\') {
                if (i >= s.size()) fail("escape incompleto");
                char e = s[i++];
                switch (e) {
                    case 'n': out += '\n'; break;
                    case 't': out += '\t'; break;
                    case 'r': out += '\r'; break;
                    case 'b': out += '\b'; break;
                    case 'f': out += '\f'; break;
                    case '"': out += '"'; break;
                    case '\\': out += '\\'; break;
                    case '/': out += '/'; break;
                    case 'u': {
                        if (i + 4 > s.size()) fail("unicode incompleto");
                        const unsigned code = static_cast<unsigned>(std::strtoul(s.substr(i, 4).c_str(), nullptr, 16));
                        i += 4;
                        if (code < 0x80) {
                            out += static_cast<char>(code);
                        } else if (code < 0x800) {
                            out += static_cast<char>(0xC0 | (code >> 6));
                            out += static_cast<char>(0x80 | (code & 0x3F));
                        } else {
                            out += static_cast<char>(0xE0 | (code >> 12));
                            out += static_cast<char>(0x80 | ((code >> 6) & 0x3F));
                            out += static_cast<char>(0x80 | (code & 0x3F));
                        }
                        break;
                    }
                    default: fail("escape inválido");
                }
            } else {
                out += c;
            }
        }
        if (i >= s.size()) fail("string não terminada");
        ++i;
        return out;
    }

    Value parseValue() {
        const char c = peek();
        if (c == '{') {
            ++i;
            Value v = Value::object();
            if (peek() == '}') { ++i; return v; }
            for (;;) {
                std::string key = parseString();
                if (peek() != ':') fail("esperado ':'");
                ++i;
                v[key] = parseValue();
                const char d = peek();
                if (d == ',') { ++i; continue; }
                if (d == '}') { ++i; break; }
                fail("esperado ',' ou '}'");
            }
            return v;
        }
        if (c == '[') {
            ++i;
            Value v = Value::array();
            if (peek() == ']') { ++i; return v; }
            for (;;) {
                v.push(parseValue());
                const char d = peek();
                if (d == ',') { ++i; continue; }
                if (d == ']') { ++i; break; }
                fail("esperado ',' ou ']'");
            }
            return v;
        }
        if (c == '"') return Value::ofString(parseString());
        if (consume("true")) return Value::ofBool(true);
        if (consume("false")) return Value::ofBool(false);
        if (consume("null")) return Value::null();
        if (c == '-' || std::isdigit(static_cast<unsigned char>(c))) {
            const size_t start = i;
            if (s[i] == '-') ++i;
            while (i < s.size() && (std::isdigit(static_cast<unsigned char>(s[i])) ||
                   s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) ++i;
            return Value::ofNumber(std::strtod(s.substr(start, i - start).c_str(), nullptr));
        }
        fail("valor inesperado");
    }
};

} // namespace

Value Value::parse(const std::string& text) {
    Parser p(text);
    Value v = p.parseValue();
    p.ws();
    if (p.i != text.size()) {
        throw std::runtime_error("JSON: conteúdo extra após o valor");
    }
    return v;
}

} // namespace VelvX::Json