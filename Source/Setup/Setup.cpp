/**
 * @file Setup.cpp
 * @brief Instalador grafico nativo (Win32) da VelvX Engine — arquivo unico.
 *
 * Wizard: Boas-vindas -> Licenca -> Diretorio -> Opcoes -> Instalacao -> Fim.
 * O payload (zip portable) vai embutido como recurso RCDATA e e extraido com
 * miniz. Cria atalhos (IShellLink), associa .velv/.ui e registra entrada de
 * desinstalacao em HKCU (Adicionar/Remover Programas). Suporta /uninstall.
 * Sem dependencia de OpenGL/GLFW/DLLs da engine: roda standalone.
 */

#include <windows.h>
#include <commctrl.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <objbase.h>
#include <string>
#include <vector>
#include <memory>
#include <cstdio>

#include "resource.h"
#include "miniz.h"

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "advapi32.lib")

namespace {

constexpr wchar_t kAppName[] = L"VelvX Engine";
constexpr wchar_t kVersion[] = L"1.0.0";
constexpr wchar_t kPublisher[] = L"NoeXVel";
constexpr wchar_t kUninstallKey[] =
    L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\VelvXEngine";

constexpr int IDC_BACK = 1001, IDC_NEXT = 1002, IDC_CANCEL = 1003;
constexpr int IDC_DIR = 1010, IDC_BROWSE = 1011;
constexpr int CHK_ACCEPT = 1020, CHK_DESKTOP = 1021, CHK_START = 1022,
              CHK_ASSOC = 1023, CHK_LAUNCH = 1024;
constexpr int IDC_PROGRESS = 1030, IDC_STATUS = 1031, IDC_LICENSE = 1032;
constexpr int IDC_WELCOME_TXT = 1040, IDC_DIRLABEL = 1041, IDC_OPTLABEL = 1042,
              IDC_FINTXT = 1043;

constexpr UINT WM_APP_PROGRESS = WM_APP + 1;
constexpr UINT WM_APP_DONE = WM_APP + 2;

enum Page { PgWelcome, PgLicense, PgDir, PgOptions, PgInstall, PgFinish };

struct State {
    HWND hwnd = nullptr;
    HFONT fontUi = nullptr, fontBig = nullptr, fontSmall = nullptr;
    HBRUSH bg = nullptr, bgEdit = nullptr;
    HICON icon = nullptr;
    int page = PgWelcome;
    bool installing = false;
    bool doneOk = false;
    wchar_t dir[MAX_PATH] = L"";
    bool optDesktop = true, optStart = true, optAssoc = true, optLaunch = true;
    bool accept = false;
    HWND back = nullptr, next = nullptr, cancel = nullptr;
    HWND dirEdit = nullptr, browse = nullptr;
    HWND chkAccept = nullptr, chkDesktop = nullptr, chkStart = nullptr,
         chkAssoc = nullptr, chkLaunch = nullptr;
    HWND progress = nullptr, status = nullptr, license = nullptr;
    HWND welcomeTxt = nullptr, dirLabel = nullptr, optLabel = nullptr, finTxt = nullptr;
};

State g;

const wchar_t* kLicense =
    L"MIT License\r\n\r\n"
    L"Copyright (c) 2026 VelvX Engine contributors\r\n\r\n"
    L"Permission is hereby granted, free of charge, to any person obtaining a copy "
    L"of this software and associated documentation files (the \"Software\"), to deal "
    L"in the Software without restriction, including without limitation the rights "
    L"to use, copy, modify, merge, publish, distribute, sublicense, and/or sell "
    L"copies of the Software, and to permit persons to whom the Software is "
    L"furnished to do so, subject to the following conditions:\r\n\r\n"
    L"The above copyright notice and this permission notice shall be included in all "
    L"copies or substantial portions of the Software.\r\n\r\n"
    L"THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR "
    L"IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, "
    L"FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE "
    L"AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER "
    L"LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, "
    L"OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE "
    L"SOFTWARE.";

std::wstring DefaultDir() {
    wchar_t base[MAX_PATH] = L"";
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, base))) {
        return std::wstring(base) + L"\\VelvXEngine";
    }
    return L"C:\\VelvXEngine";
}

std::wstring SelfPath() {
    wchar_t buf[MAX_PATH] = L"";
    GetModuleFileNameW(nullptr, buf, MAX_PATH);
    return buf;
}

void MakeDirs(const std::wstring& path) {
    SHCreateDirectoryExW(nullptr, path.c_str(), nullptr);
}

bool CreateShortcut(const std::wstring& lnk, const std::wstring& target,
                    const std::wstring& workdir, const std::wstring& icon,
                    const std::wstring& args) {
    IShellLinkW* link = nullptr;
    if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER,
                                IID_IShellLinkW, reinterpret_cast<void**>(&link))))
        return false;
    link->SetPath(target.c_str());
    link->SetWorkingDirectory(workdir.c_str());
    if (!icon.empty()) link->SetIconLocation(icon.c_str(), 0);
    if (!args.empty()) link->SetArguments(args.c_str());
    IPersistFile* pf = nullptr;
    bool ok = false;
    if (SUCCEEDED(link->QueryInterface(IID_IPersistFile, reinterpret_cast<void**>(&pf)))) {
        ok = SUCCEEDED(pf->Save(lnk.c_str(), TRUE));
        pf->Release();
    }
    link->Release();
    return ok;
}

std::wstring StartMenuDir() {
    wchar_t base[MAX_PATH] = L"";
    SHGetFolderPathW(nullptr, CSIDL_PROGRAMS, nullptr, 0, base);
    return std::wstring(base) + L"\\VelvX Engine";
}

std::wstring DesktopDir() {
    wchar_t base[MAX_PATH] = L"";
    SHGetFolderPathW(nullptr, CSIDL_DESKTOPDIRECTORY, nullptr, 0, base);
    return base;
}

void WriteRegString(HKEY root, const wchar_t* sub, const wchar_t* name,
                    const std::wstring& value) {
    HKEY k = nullptr;
    if (RegCreateKeyExW(root, sub, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr)
        == ERROR_SUCCESS) {
        RegSetValueExW(k, name, 0, REG_SZ,
                       reinterpret_cast<const BYTE*>(value.c_str()),
                       static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
        RegCloseKey(k);
    }
}

void WriteRegDword(HKEY root, const wchar_t* sub, const wchar_t* name, DWORD v) {
    HKEY k = nullptr;
    if (RegCreateKeyExW(root, sub, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr)
        == ERROR_SUCCESS) {
        RegSetValueExW(k, name, 0, REG_DWORD, reinterpret_cast<const BYTE*>(&v), sizeof(v));
        RegCloseKey(k);
    }
}

void RegisterUninstall(const std::wstring& dir) {
    const std::wstring exe = dir + L"\\VelvXSetup.exe";
    WriteRegString(HKEY_CURRENT_USER, kUninstallKey, L"DisplayName", kAppName);
    WriteRegString(HKEY_CURRENT_USER, kUninstallKey, L"DisplayVersion", kVersion);
    WriteRegString(HKEY_CURRENT_USER, kUninstallKey, L"Publisher", kPublisher);
    WriteRegString(HKEY_CURRENT_USER, kUninstallKey, L"DisplayIcon", dir + L"\\Icon.ico");
    WriteRegString(HKEY_CURRENT_USER, kUninstallKey, L"InstallLocation", dir);
    WriteRegString(HKEY_CURRENT_USER, kUninstallKey, L"UninstallString",
                   L"\"" + exe + L"\" /uninstall");
    WriteRegDword(HKEY_CURRENT_USER, kUninstallKey, L"NoModify", 1);
    WriteRegDword(HKEY_CURRENT_USER, kUninstallKey, L"NoRepair", 1);
}

void RegisterAssociations(const std::wstring& dir) {
    WriteRegString(HKEY_CURRENT_USER, L"Software\\Classes\\.velv", nullptr, L"VelvX.VelvScript");
    WriteRegString(HKEY_CURRENT_USER, L"Software\\Classes\\VelvX.VelvScript", nullptr, L"Velv Language Script");
    WriteRegString(HKEY_CURRENT_USER, L"Software\\Classes\\VelvX.VelvScript\\shell\\open\\command",
                   nullptr, L"\"" + dir + L"\\VelvXScriptIDE.exe\" \"%1\"");
    WriteRegString(HKEY_CURRENT_USER, L"Software\\Classes\\.ui", nullptr, L"VelvX.UIPrefab");
    WriteRegString(HKEY_CURRENT_USER, L"Software\\Classes\\VelvX.UIPrefab", nullptr, L"VelvX UI Prefab");
    WriteRegString(HKEY_CURRENT_USER, L"Software\\Classes\\VelvX.UIPrefab\\shell\\open\\command",
                   nullptr, L"\"" + dir + L"\\VelvXUIEditor.exe\" \"%1\"");
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
}

void RemoveAssociations() {
    RegDeleteTreeW(HKEY_CURRENT_USER, L"Software\\Classes\\.velv");
    RegDeleteTreeW(HKEY_CURRENT_USER, L"Software\\Classes\\VelvX.VelvScript");
    RegDeleteTreeW(HKEY_CURRENT_USER, L"Software\\Classes\\.ui");
    RegDeleteTreeW(HKEY_CURRENT_USER, L"Software\\Classes\\VelvX.UIPrefab");
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
}

void CreateShortcuts(const std::wstring& dir) {
    const std::wstring icon = dir + L"\\Icon.ico";
    const std::wstring launcher = dir + L"\\VelvXLauncher.exe";
    const std::wstring editor = dir + L"\\VelvXEditor.exe";
    if (g.optDesktop)
        CreateShortcut(DesktopDir() + L"\\VelvX Engine.lnk", launcher, dir, icon, L"");
    if (g.optStart) {
        const std::wstring sm = StartMenuDir();
        MakeDirs(sm);
        CreateShortcut(sm + L"\\VelvX Engine.lnk", launcher, dir, icon, L"");
        CreateShortcut(sm + L"\\VelvX Editor.lnk", editor, dir, icon, L"");
        CreateShortcut(sm + L"\\Desinstalar VelvX Engine.lnk",
                       dir + L"\\VelvXSetup.exe", dir, icon, L"/uninstall");
    }
}

void RemoveShortcuts() {
    DeleteFileW((DesktopDir() + L"\\VelvX Engine.lnk").c_str());
    const std::wstring sm = StartMenuDir();
    DeleteFileW((sm + L"\\VelvX Engine.lnk").c_str());
    DeleteFileW((sm + L"\\VelvX Editor.lnk").c_str());
    DeleteFileW((sm + L"\\Desinstalar VelvX Engine.lnk").c_str());
    RemoveDirectoryW(sm.c_str());
}

// ---------------------------------------------------------------------------
// Extracao do payload (thread de trabalho)
// ---------------------------------------------------------------------------
struct ExtractJob {
    std::wstring dir;
};

bool ExtractPayload(const std::wstring& dir, HWND notify) {
    HRSRC rs = FindResourceW(nullptr, MAKEINTRESOURCEW(IDR_PAYLOAD), RT_RCDATA);
    if (!rs) return false;
    HGLOBAL hg = LoadResource(nullptr, rs);
    const void* data = LockResource(hg);
    DWORD size = SizeofResource(nullptr, rs);
    mz_zip_archive zip{};
    if (!mz_zip_reader_init_mem(&zip, data, size, 0)) return false;
    const mz_uint total = mz_zip_reader_get_num_files(&zip);
    MakeDirs(dir);
    bool ok = true;
    for (mz_uint i = 0; i < total; ++i) {
        char name[MZ_ZIP_MAX_ARCHIVE_FILENAME_SIZE] = "";
        mz_zip_reader_get_filename(&zip, i, name, sizeof(name));
        std::wstring rel;
        int wlen = MultiByteToWideChar(CP_UTF8, 0, name, -1, nullptr, 0);
        if (wlen > 0) {
            rel.resize(wlen - 1);
            MultiByteToWideChar(CP_UTF8, 0, name, -1, rel.data(), wlen);
        }
        for (auto& c : rel) if (c == L'/') c = L'\\';
        const std::wstring dest = dir + L"\\" + rel;
        if (mz_zip_reader_is_file_a_directory(&zip, i)) {
            MakeDirs(dest);
        } else {
            MakeDirs(dest.substr(0, dest.find_last_of(L'\\')));
            std::string destA;
            int nlen = WideCharToMultiByte(CP_UTF8, 0, dest.c_str(), -1, nullptr, 0, nullptr, nullptr);
            if (nlen > 0) {
                destA.resize(nlen - 1);
                WideCharToMultiByte(CP_UTF8, 0, dest.c_str(), -1, destA.data(), nlen, nullptr, nullptr);
            }
            if (!mz_zip_reader_extract_to_file(&zip, i, destA.c_str(), 0)) ok = false;
        }
        if (notify) PostMessageW(notify, WM_APP_PROGRESS, i + 1, total);
    }
    mz_zip_reader_end(&zip);
    return ok;
}

DWORD WINAPI ExtractThread(LPVOID param) {
    std::unique_ptr<ExtractJob> job(static_cast<ExtractJob*>(param));
    const bool ok = ExtractPayload(job->dir, g.hwnd);
    PostMessageW(g.hwnd, WM_APP_DONE, ok ? 1 : 0, 0);
    return ok ? 0 : 1;
}

void FinalizeInstall() {
    const std::wstring dir = g.dir;
    CopyFileW(SelfPath().c_str(), (dir + L"\\VelvXSetup.exe").c_str(), FALSE);
    CreateShortcuts(dir);
    if (g.optAssoc) RegisterAssociations(dir);
    RegisterUninstall(dir);
}

// ---------------------------------------------------------------------------
// UI
// ---------------------------------------------------------------------------
void SetPage(int p);

void UpdateButtons() {
    const bool inst = g.page == PgInstall;
    EnableWindow(g.back, g.page > PgWelcome && !inst && g.page != PgFinish);
    EnableWindow(g.cancel, !inst);
    if (g.page == PgFinish) {
        SetWindowTextW(g.next, L"Concluir");
        EnableWindow(g.next, TRUE);
    } else if (inst) {
        SetWindowTextW(g.next, L"Aguarde...");
        EnableWindow(g.next, FALSE);
    } else {
        SetWindowTextW(g.next, L"Avançar >");
        EnableWindow(g.next, g.page != PgLicense || g.accept);
    }
    SetWindowTextW(g.cancel, L"Cancelar");
}

void ShowPageControls() {
    const int p = g.page;
    auto show = [&](HWND h, bool on) { if (h) ShowWindow(h, on ? SW_SHOW : SW_HIDE); };
    show(g.welcomeTxt, p == PgWelcome);
    show(g.license, p == PgLicense);
    show(g.chkAccept, p == PgLicense);
    show(g.dirLabel, p == PgDir);
    show(g.dirEdit, p == PgDir);
    show(g.browse, p == PgDir);
    show(g.optLabel, p == PgOptions);
    show(g.chkDesktop, p == PgOptions);
    show(g.chkStart, p == PgOptions);
    show(g.chkAssoc, p == PgOptions);
    show(g.progress, p == PgInstall);
    show(g.status, p == PgInstall);
    show(g.finTxt, p == PgFinish);
    show(g.chkLaunch, p == PgFinish);
}

void SetPage(int p) {
    g.page = p;
    ShowPageControls();
    UpdateButtons();
    if (p == PgInstall && !g.installing) {
        g.installing = true;
        SendMessageW(g.progress, PBM_SETRANGE32, 0, 100);
        SendMessageW(g.progress, PBM_SETPOS, 0, 0);
        SetWindowTextW(g.status, L"Extraindo arquivos...");
        auto* job = new ExtractJob{ g.dir };
        HANDLE t = CreateThread(nullptr, 0, ExtractThread, job, 0, nullptr);
        if (t) CloseHandle(t);
        else { g.installing = false; PostMessageW(g.hwnd, WM_APP_DONE, 0, 0); }
    }
    InvalidateRect(g.hwnd, nullptr, TRUE);
}

void DrawHeader(HDC dc, const RECT& rc) {
    HBRUSH band = CreateSolidBrush(RGB(24, 26, 32));
    RECT bandRc = { rc.left, rc.top, rc.right, rc.top + 76 };
    FillRect(dc, &bandRc, band);
    DeleteObject(band);
    if (g.icon) DrawIconEx(dc, 20, 16, g.icon, 44, 44, 0, nullptr, DI_NORMAL);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, RGB(235, 238, 245));
    HFONT old = static_cast<HFONT>(SelectObject(dc, g.fontBig));
    TextOutW(dc, 78, 16, kAppName, static_cast<int>(wcslen(kAppName)));
    SelectObject(dc, g.fontSmall);
    SetTextColor(dc, RGB(150, 158, 172));
    const wchar_t* sub = L"Instalador  ·  versão 1.0.0  ·  motor de jogos 2D/3D";
    TextOutW(dc, 78, 44, sub, static_cast<int>(wcslen(sub)));
    SelectObject(dc, old);
    HPEN pen = CreatePen(PS_SOLID, 1, RGB(64, 200, 160));
    HPEN oldPen = static_cast<HPEN>(SelectObject(dc, pen));
    MoveToEx(dc, rc.left, 76, nullptr);
    LineTo(dc, rc.right, 76);
    SelectObject(dc, oldPen);
    DeleteObject(pen);
}

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_CREATE: {
        g.hwnd = hwnd;
        g.bg = CreateSolidBrush(RGB(32, 34, 40));
        g.bgEdit = CreateSolidBrush(RGB(24, 26, 32));
        g.icon = LoadIconW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDI_APPICON));
        g.fontUi = CreateFontW(-15, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET,
                               OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                               DEFAULT_PITCH, L"Segoe UI");
        g.fontBig = CreateFontW(-24, 0, 0, 0, FW_SEMIBOLD, 0, 0, 0, DEFAULT_CHARSET,
                                OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                                DEFAULT_PITCH, L"Segoe UI");
        g.fontSmall = CreateFontW(-13, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET,
                                 OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                                 DEFAULT_PITCH, L"Segoe UI");
        auto mk = [&](const wchar_t* cls, const wchar_t* txt, DWORD style,
                      int x, int y, int w, int h, int id) -> HWND {
            HWND c = CreateWindowExW(0, cls, txt, WS_CHILD | WS_VISIBLE | style, x, y, w, h,
                                     hwnd, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                                     GetModuleHandleW(nullptr), nullptr);
            SendMessageW(c, WM_SETFONT, reinterpret_cast<WPARAM>(g.fontUi), TRUE);
            return c;
        };
        g.welcomeTxt = mk(L"STATIC",
            L"Bem-vindo ao instalador da VelvX Engine.\r\n\r\n"
            L"Este assistente instala a engine completa: Editor, Velv Language IDE, "
            L"UI Editor, Player, demos e documentação.\r\n\r\n"
            L"A instalação é por usuário (não requer administrador) e inclui "
            L"desinstalador integrado ao Windows.",
            SS_LEFT, 24, 100, 600, 200, IDC_WELCOME_TXT);
        g.license = mk(L"EDIT", kLicense,
            WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL | WS_BORDER,
            24, 96, 600, 250, IDC_LICENSE);
        g.chkAccept = mk(L"BUTTON", L"Eu li e aceito os termos da licença MIT",
            BS_AUTOCHECKBOX, 24, 356, 600, 24, CHK_ACCEPT);
        g.dirLabel = mk(L"STATIC", L"Pasta de instalação:", SS_LEFT, 24, 100, 600, 20, IDC_DIRLABEL);
        g.dirEdit = mk(L"EDIT", g.dir, ES_AUTOHSCROLL | WS_BORDER, 24, 126, 480, 26, IDC_DIR);
        g.browse = mk(L"BUTTON", L"Procurar...", BS_PUSHBUTTON, 514, 125, 110, 28, IDC_BROWSE);
        g.optLabel = mk(L"STATIC", L"Opções de integração:", SS_LEFT, 24, 100, 600, 20, IDC_OPTLABEL);
        g.chkDesktop = mk(L"BUTTON", L"Criar atalho na Área de Trabalho", BS_AUTOCHECKBOX, 24, 130, 600, 24, CHK_DESKTOP);
        g.chkStart = mk(L"BUTTON", L"Criar atalhos no Menu Iniciar", BS_AUTOCHECKBOX, 24, 160, 600, 24, CHK_START);
        g.chkAssoc = mk(L"BUTTON", L"Associar arquivos .velv e .ui à VelvX", BS_AUTOCHECKBOX, 24, 190, 600, 24, CHK_ASSOC);
        g.progress = mk(PROGRESS_CLASSW, L"", PBS_SMOOTH, 24, 150, 600, 22, IDC_PROGRESS);
        g.status = mk(L"STATIC", L"", SS_LEFT, 24, 184, 600, 20, IDC_STATUS);
        g.finTxt = mk(L"STATIC",
            L"Instalação concluída com sucesso!\r\n\r\n"
            L"A VelvX Engine está pronta para uso. Abra o Launcher pelo atalho "
            L"criado ou pelo Menu Iniciar.",
            SS_LEFT, 24, 100, 600, 120, IDC_FINTXT);
        g.chkLaunch = mk(L"BUTTON", L"Abrir o VelvX Launcher ao sair", BS_AUTOCHECKBOX, 24, 230, 600, 24, CHK_LAUNCH);
        g.back = mk(L"BUTTON", L"< Voltar", BS_OWNERDRAW, 304, 437, 100, 32, IDC_BACK);
        g.next = mk(L"BUTTON", L"Avançar >", BS_OWNERDRAW, 412, 437, 100, 32, IDC_NEXT);
        g.cancel = mk(L"BUTTON", L"Cancelar", BS_OWNERDRAW, 520, 437, 100, 32, IDC_CANCEL);
        SendMessageW(g.chkDesktop, BM_SETCHECK, BST_CHECKED, 0);
        SendMessageW(g.chkStart, BM_SETCHECK, BST_CHECKED, 0);
        SendMessageW(g.chkAssoc, BM_SETCHECK, BST_CHECKED, 0);
        SendMessageW(g.chkLaunch, BM_SETCHECK, BST_CHECKED, 0);
        SetPage(PgWelcome);
        return 0;
    }
    case WM_CTLCOLORSTATIC: {
        HDC dc = reinterpret_cast<HDC>(wp);
        // EDIT read-only envia WM_CTLCOLORSTATIC: precisa de fundo opaco,
        // senao o texto repinta sobre pixels antigos (fantasma).
        if (reinterpret_cast<HWND>(lp) == g.license) {
            SetBkColor(dc, RGB(24, 26, 32));
            SetTextColor(dc, RGB(235, 238, 245));
            return reinterpret_cast<LRESULT>(g.bgEdit);
        }
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, RGB(225, 228, 235));
        return reinterpret_cast<LRESULT>(g.bg);
    }
    case WM_CTLCOLOREDIT: {
        HDC dc = reinterpret_cast<HDC>(wp);
        SetBkColor(dc, RGB(24, 26, 32));
        SetTextColor(dc, RGB(235, 238, 245));
        return reinterpret_cast<LRESULT>(g.bgEdit);
    }
    case WM_CTLCOLORBTN: {
        HDC dc = reinterpret_cast<HDC>(wp);
        SetBkColor(dc, RGB(32, 34, 40));
        SetTextColor(dc, RGB(225, 228, 235));
        return reinterpret_cast<LRESULT>(g.bg);
    }
    case WM_DRAWITEM: {
        const DRAWITEMSTRUCT* di = reinterpret_cast<const DRAWITEMSTRUCT*>(lp);
        if (di->CtlType != ODT_BUTTON) break;
        HBRUSH b = CreateSolidBrush(di->itemState & ODS_DISABLED ? RGB(48, 50, 58)
                                  : (di->itemID == IDC_NEXT ? RGB(38, 160, 120) : RGB(52, 55, 64)));
        FillRect(di->hDC, &di->rcItem, b);
        DeleteObject(b);
        SetBkMode(di->hDC, TRANSPARENT);
        SetTextColor(di->hDC, di->itemState & ODS_DISABLED ? RGB(120, 124, 132) : RGB(240, 242, 246));
        wchar_t txt[64] = L"";
        GetWindowTextW(di->hwndItem, txt, 64);
        HFONT old = static_cast<HFONT>(SelectObject(di->hDC, g.fontUi));
        RECT r = di->rcItem;
        DrawTextW(di->hDC, txt, -1, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        SelectObject(di->hDC, old);
        return TRUE;
    }
    case WM_PAINT: {
        PAINTSTRUCT ps;
        HDC dc = BeginPaint(hwnd, &ps);
        RECT rc; GetClientRect(hwnd, &rc);
        FillRect(dc, &rc, g.bg);
        DrawHeader(dc, rc);
        EndPaint(hwnd, &ps);
        return 0;
    }
    case WM_COMMAND: {
        const int id = LOWORD(wp);
        if (id == IDC_CANCEL) {
            if (g.installing) return 0;
            if (MessageBoxW(hwnd, L"Cancelar a instalação?", kAppName,
                            MB_YESNO | MB_ICONQUESTION) == IDYES)
                DestroyWindow(hwnd);
            return 0;
        }
        if (id == IDC_BROWSE) {
            wchar_t path[MAX_PATH] = L"";
            BROWSEINFOW bi{};
            bi.hwndOwner = hwnd;
            bi.lpszTitle = L"Escolha a pasta de instalação";
            bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
            PIDLIST_ABSOLUTE pid = SHBrowseForFolderW(&bi);
            if (pid) {
                if (SHGetPathFromIDListW(pid, path)) SetWindowTextW(g.dirEdit, path);
                CoTaskMemFree(pid);
            }
            return 0;
        }
        if (id == CHK_ACCEPT) {
            g.accept = SendMessageW(g.chkAccept, BM_GETCHECK, 0, 0) == BST_CHECKED;
            UpdateButtons();
            return 0;
        }
        if (id == IDC_BACK) { if (g.page > PgWelcome) SetPage(g.page - 1); return 0; }
        if (id == IDC_NEXT) {
            if (g.page == PgWelcome) { SetPage(PgLicense); return 0; }
            if (g.page == PgLicense) { if (g.accept) SetPage(PgDir); return 0; }
            if (g.page == PgDir) {
                wchar_t buf[MAX_PATH] = L"";
                GetWindowTextW(g.dirEdit, buf, MAX_PATH);
                if (wcslen(buf) < 3) {
                    MessageBoxW(hwnd, L"Escolha uma pasta válida.", kAppName, MB_ICONWARNING);
                    return 0;
                }
                wcscpy_s(g.dir, buf);
                SetPage(PgOptions);
                return 0;
            }
            if (g.page == PgOptions) {
                g.optDesktop = SendMessageW(g.chkDesktop, BM_GETCHECK, 0, 0) == BST_CHECKED;
                g.optStart = SendMessageW(g.chkStart, BM_GETCHECK, 0, 0) == BST_CHECKED;
                g.optAssoc = SendMessageW(g.chkAssoc, BM_GETCHECK, 0, 0) == BST_CHECKED;
                SetPage(PgInstall);
                return 0;
            }
            if (g.page == PgFinish) {
                g.optLaunch = SendMessageW(g.chkLaunch, BM_GETCHECK, 0, 0) == BST_CHECKED;
                if (g.optLaunch) {
                    const std::wstring launcher = std::wstring(g.dir) + L"\\VelvXLauncher.exe";
                    ShellExecuteW(hwnd, L"open", launcher.c_str(), nullptr, g.dir, SW_SHOWNORMAL);
                }
                DestroyWindow(hwnd);
                return 0;
            }
            return 0;
        }
        return 0;
    }
    case WM_APP_PROGRESS: {
        const int cur = static_cast<int>(wp), total = static_cast<int>(lp);
        if (total > 0)
            SendMessageW(g.progress, PBM_SETPOS, (cur * 100) / total, 0);
        wchar_t buf[128];
        swprintf_s(buf, L"Extraindo arquivos...  %d / %d", cur, total);
        SetWindowTextW(g.status, buf);
        return 0;
    }
    case WM_APP_DONE: {
        g.installing = false;
        g.doneOk = wp != 0;
        if (g.doneOk) {
            FinalizeInstall();
            SetPage(PgFinish);
        } else {
            MessageBoxW(hwnd, L"Falha ao extrair os arquivos da instalação.",
                        kAppName, MB_ICONERROR);
            SetPage(PgOptions);
        }
        return 0;
    }
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

// ---------------------------------------------------------------------------
// Desinstalacao
// ---------------------------------------------------------------------------
bool RemoveInstallDir(const std::wstring& dir) {
    // pFrom exige lista duplo-nul-terminada: dir + '\0' + terminador implícito.
    std::wstring from = dir;
    from.push_back(L'\0');
    SHFILEOPSTRUCTW op{};
    op.wFunc = FO_DELETE;
    op.pFrom = from.c_str();
    op.fFlags = FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT;
    const int res = SHFileOperationW(&op);
    return res == 0 && !op.fAnyOperationsAborted;
}

int RunUninstall(bool stage2) {
    wchar_t dir[MAX_PATH] = L"";
    HKEY k = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, kUninstallKey, 0, KEY_QUERY_VALUE, &k) == ERROR_SUCCESS) {
        DWORD sz = sizeof(dir);
        RegQueryValueExW(k, L"InstallLocation", nullptr, nullptr, reinterpret_cast<BYTE*>(dir), &sz);
        RegCloseKey(k);
    }
    if (!stage2) {
        if (MessageBoxW(nullptr, L"Remover a VelvX Engine e todos os seus arquivos?",
                        kAppName, MB_YESNO | MB_ICONQUESTION) != IDYES)
            return 0;
        // Re-executa a partir do TEMP para poder apagar a propria pasta.
        wchar_t tmp[MAX_PATH] = L"";
        GetTempPathW(MAX_PATH, tmp);
        const std::wstring copy = std::wstring(tmp) + L"velvx_uninstall.exe";
        CopyFileW(SelfPath().c_str(), copy.c_str(), FALSE);
        ShellExecuteW(nullptr, L"open", copy.c_str(), L"/uninstall-stage2", tmp, SW_SHOWNORMAL);
        return 0;
    }
    // O CWD nao pode estar dentro da pasta removida (atalhos apontam para ela).
    wchar_t tmp2[MAX_PATH] = L"";
    GetTempPathW(MAX_PATH, tmp2);
    SetCurrentDirectoryW(tmp2);
    RemoveShortcuts();
    RemoveAssociations();
    RegDeleteTreeW(HKEY_CURRENT_USER, kUninstallKey);
    bool ok = true;
    if (wcslen(dir)) ok = RemoveInstallDir(dir);
    MessageBoxW(nullptr,
                ok ? L"VelvX Engine desinstalada."
                   : L"VelvX Engine desinstalada, mas alguns arquivos nao puderam ser "
                     L"removidos (feche a Engine e apague a pasta manualmente).",
                kAppName, ok ? MB_ICONINFORMATION : MB_ICONWARNING);
    return 0;
}

} // namespace

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE, PWSTR cmd, int show) {
    const std::wstring args = cmd ? cmd : L"";
    if (args.find(L"/uninstall-stage2") != std::wstring::npos) return RunUninstall(true);
    if (args.find(L"/uninstall") != std::wstring::npos) return RunUninstall(false);

    if (args.find(L"/S") != std::wstring::npos) {
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        std::wstring dir = DefaultDir();
        const size_t dpos = args.find(L"/D=");
        if (dpos != std::wstring::npos) dir = args.substr(dpos + 3);
        wcscpy_s(g.dir, dir.c_str());
        const bool ok = ExtractPayload(dir, nullptr);
        if (ok) FinalizeInstall();
        CoUninitialize();
        return ok ? 0 : 1;
    }

    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    INITCOMMONCONTROLSEX icc{ sizeof(icc), ICC_PROGRESS_CLASS | ICC_STANDARD_CLASSES };
    InitCommonControlsEx(&icc);

    wcscpy_s(g.dir, DefaultDir().c_str());

    WNDCLASSW wc{};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = nullptr;
    wc.lpszClassName = L"VelvXSetupWnd";
    wc.hIcon = LoadIconW(inst, MAKEINTRESOURCEW(IDI_APPICON));
    RegisterClassW(&wc);

    HWND hwnd = CreateWindowExW(0, wc.lpszClassName, L"VelvX Engine — Instalação",
                                WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
                                CW_USEDEFAULT, CW_USEDEFAULT, 660, 520,
                                nullptr, nullptr, inst, nullptr);
    if (!hwnd) return 1;
    ShowWindow(hwnd, show);
    UpdateWindow(hwnd);

    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0)) {
        if (!IsDialogMessageW(hwnd, &msg)) {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    CoUninitialize();
    return static_cast<int>(msg.wParam);
}