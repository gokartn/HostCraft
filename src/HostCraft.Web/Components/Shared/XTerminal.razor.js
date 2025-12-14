// XTerm.js integration for Blazor
let Terminal, FitAddon;

async function loadXTermLibraries() {
    if (!Terminal) {
        // Load XTerm.js from CDN
        await import('https://cdn.jsdelivr.net/npm/xterm@5.3.0/lib/xterm.js');
        Terminal = window.Terminal;
    }
    if (!FitAddon) {
        await import('https://cdn.jsdelivr.net/npm/xterm-addon-fit@0.8.0/lib/xterm-addon-fit.js');
        FitAddon = window.FitAddon.FitAddon;
    }
}

export async function initTerminal(elementId, dotNetRef) {
    try {
        await loadXTermLibraries();

        const element = document.getElementById(elementId);
        if (!element) {
            console.error(`Terminal element ${elementId} not found`);
            return null;
        }

        const terminal = new Terminal({
            cursorBlink: true,
            cursorStyle: 'block',
            fontFamily: 'Consolas, Monaco, "Courier New", monospace',
            fontSize: 14,
            lineHeight: 1.4,
            theme: {
                background: '#000000',
                foreground: '#ffffff',
                cursor: '#00ff00',
                cursorAccent: '#000000',
                selectionBackground: '#4d4d4d',
                black: '#000000',
                red: '#ff5555',
                green: '#50fa7b',
                yellow: '#f1fa8c',
                blue: '#bd93f9',
                magenta: '#ff79c6',
                cyan: '#8be9fd',
                white: '#bfbfbf',
                brightBlack: '#4d4d4d',
                brightRed: '#ff6e67',
                brightGreen: '#5af78e',
                brightYellow: '#f4f99d',
                brightBlue: '#caa9fa',
                brightMagenta: '#ff92d0',
                brightCyan: '#9aedfe',
                brightWhite: '#e6e6e6'
            },
            allowProposedApi: true,
            scrollback: 10000
        });

        const fitAddon = new FitAddon();
        terminal.loadAddon(fitAddon);

        terminal.open(element);
        fitAddon.fit();

        // Handle input
        let currentLine = '';
        terminal.onData(data => {
            if (data === '\r') { // Enter key
                terminal.write('\r\n');
                dotNetRef.invokeMethodAsync('HandleInput', currentLine + '\n');
                currentLine = '';
            } else if (data === '\u007F') { // Backspace
                if (currentLine.length > 0) {
                    currentLine = currentLine.slice(0, -1);
                    terminal.write('\b \b');
                }
            } else if (data === '\u0003') { // Ctrl+C
                terminal.write('^C\r\n');
                dotNetRef.invokeMethodAsync('HandleInput', '\u0003');
                currentLine = '';
            } else {
                currentLine += data;
                terminal.write(data);
            }
        });

        // Resize handler
        const resizeObserver = new ResizeObserver(() => {
            fitAddon.fit();
        });
        resizeObserver.observe(element);

        terminal._resizeObserver = resizeObserver;
        terminal._fitAddon = fitAddon;

        return terminal;
    } catch (error) {
        console.error('Failed to initialize XTerm:', error);
        return null;
    }
}

export function writeOutput(terminal, data) {
    if (terminal) {
        terminal.write(data);
    }
}

export function clear(terminal) {
    if (terminal) {
        terminal.clear();
    }
}

export function focus(terminal) {
    if (terminal) {
        terminal.focus();
    }
}

export function dispose(terminal) {
    if (terminal) {
        if (terminal._resizeObserver) {
            terminal._resizeObserver.disconnect();
        }
        terminal.dispose();
    }
}
