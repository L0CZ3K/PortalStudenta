/**
 * ==============================================================================
 * UniAvio - High-Performance WYSIWYG Note Editor & IDE Syntax Highlighter
 * ==============================================================================
 * Provides native-like rich-text editing with:
 * - Tokenized inline image management (preventing heavy Base64 in HTML persistence)
 * - Interactive checklist management with keyboard handling (Enter / Backspace)
 * - Multi-language syntax highlighting for code snippets (C#, Python, SQL, JS, etc.)
 * - Text styling, headings, and color highlighting
 * - Debounced change notifications via Blazor JSInterop
 * ==============================================================================
 */

window.notesEditor = {
    debounceTimer: null,
    currentDotNetHelper: null,
    editorEl: null,
    currentNoteId: null,
    savedRange: null,

    /**
     * Saves the current active selection range inside the editor.
     */
    saveSelection: function () {
        var sel = window.getSelection();
        if (sel && sel.rangeCount > 0 && this.editorEl) {
            var r = sel.getRangeAt(0);
            if (this.editorEl.contains(r.commonAncestorContainer)) {
                this.savedRange = r.cloneRange();
            }
        }
    },

    /**
     * Sanitizes editor HTML before serializing to SQLite storage.
     * Strips heavy inline Base64 data URIs from image elements while preserving
     * the 'data-attachment-id' attribute for lazy resolution on load.
     */
    getCleanContent: function () {
        if (!this.editorEl) return '';
        var clone = this.editorEl.cloneNode(true);

        // Usuń ciężkie atrybuty src z obrazów powiązanych z załącznikami (pozostawiając token data-attachment-id)
        var inlineImages = clone.querySelectorAll('img[data-attachment-id]');
        for (var i = 0; i < inlineImages.length; i++) {
            inlineImages[i].removeAttribute('src');
        }

        // Zabezpieczenie przed ciężkimi inline data:image/... wklejonymi bezpośrednio ze schowka
        var allImgs = clone.querySelectorAll('img');
        for (var j = 0; j < allImgs.length; j++) {
            var src = allImgs[j].getAttribute('src') || '';
            if (src.length > 500 && src.indexOf('data:image') === 0) {
                allImgs[j].removeAttribute('src');
            }
        }
        return clone.innerHTML;
    },

    /**
     * Notifies Blazor C# component that editor content was modified.
     */
    notifyContentChanged: function () {
        if (this.currentDotNetHelper && this.editorEl) {
            var cleanHtml = this.getCleanContent();
            this.currentDotNetHelper.invokeMethodAsync('OnEditorContentChanged', this.currentNoteId || '', cleanHtml);
        }
    },

    /**
     * Initializes the editor event handlers, keyboard shortcuts, and selection monitors.
     */
    init: function (editorEl, dotNetHelper, noteId) {
        if (!editorEl) return;
        this.editorEl = editorEl;
        this.currentDotNetHelper = dotNetHelper;
        if (noteId !== undefined && noteId !== null) {
            this.currentNoteId = noteId;
        }

        var self = this;

        // Nasłuchiwanie zmian w treści z debouncem 500ms (ochrona przed lagowaniem UI przy pisaniu)
        editorEl.oninput = function () {
            clearTimeout(self.debounceTimer);
            self.debounceTimer = setTimeout(function () {
                self.notifyContentChanged();
            }, 500);
        };

        // Aktualizacja stanów przycisków paska narzędzi i utrwalanie pozycji kursora
        document.addEventListener('selectionchange', function () {
            self.updateToolbarStates();
            self.saveSelection();
        });

        editorEl.addEventListener('mouseup', function () { self.saveSelection(); });
        editorEl.addEventListener('keyup', function () { self.saveSelection(); });
        editorEl.addEventListener('blur', function () { self.saveSelection(); });
        editorEl.addEventListener('touchend', function () { self.saveSelection(); });

        // Wklejanie ze schowka (obsługa wklejania obrazów przez Cmd+V / Ctrl+V)
        editorEl.addEventListener('paste', function (e) {
            var clipboardData = e.clipboardData || window.clipboardData;
            if (!clipboardData) return;

            var items = clipboardData.items;
            if (items) {
                for (var i = 0; i < items.length; i++) {
                    if (items[i].type && items[i].type.indexOf('image/') === 0) {
                        var file = items[i].getAsFile();
                        if (file) {
                            e.preventDefault();
                            self.saveSelection();
                            var reader = new FileReader();
                            reader.onload = function (evt) {
                                var base64 = evt.target.result;
                                if (self.currentDotNetHelper) {
                                    self.currentDotNetHelper.invokeMethodAsync('OnClipboardImagePasted', base64, file.type, file.name || 'wklejony_obraz.png');
                                }
                            };
                            reader.readAsDataURL(file);
                            return;
                        }
                    }
                }
            }
        });

        // Obsługa klawiszy (Enter w checklistach, Tab w blokach kodu, skróty Cmd/Ctrl)
        editorEl.onkeydown = function (e) {
            var isMac = navigator.platform.toUpperCase().indexOf('MAC') >= 0;
            var modKey = isMac ? e.metaKey : e.ctrlKey;

            // Skrót aparatu zdalnego (Cmd+Shift+C lub Cmd+Shift+P)
            if (modKey && e.shiftKey && (e.key === 'c' || e.key === 'C' || e.key === 'p' || e.key === 'P')) {
                e.preventDefault();
                self.saveSelection();
                if (self.currentDotNetHelper) {
                    self.currentDotNetHelper.invokeMethodAsync('TriggerRemoteCameraShortcut');
                }
                return;
            }

            // Klawisze skrótów formatowania (Cmd+B, Cmd+I, Cmd+U)
            if (modKey && !e.shiftKey && !e.altKey) {
                if (e.key === 'b' || e.key === 'B') {
                    e.preventDefault();
                    self.exec('bold');
                    return;
                }
                if (e.key === 'i' || e.key === 'I') {
                    e.preventDefault();
                    self.exec('italic');
                    return;
                }
                if (e.key === 'u' || e.key === 'U') {
                    e.preventDefault();
                    self.exec('underline');
                    return;
                }
            }

            // Tab w bloku kodu (wstawia 4 spacje zamiast gubić fokus)
            if (e.key === 'Tab') {
                var sel = window.getSelection();
                if (sel && sel.anchorNode) {
                    var codeNode = self.closest(sel.anchorNode, 'code');
                    if (codeNode) {
                        e.preventDefault();
                        document.execCommand('insertText', false, '    ');
                        return;
                    }
                }
            }

            // Backspace w checkliście (gdy pusta, usuwa checkboks i zamienia w zwykły akapit)
            if (e.key === 'Backspace') {
                var selB = window.getSelection();
                if (selB && selB.anchorNode) {
                    var checkItemB = self.closest(selB.anchorNode, '.notes-checklist-item');
                    if (checkItemB) {
                        var textSpanB = checkItemB.querySelector('span');
                        if (textSpanB && (textSpanB.innerText.trim() === '' || textSpanB.innerText === '\u00A0')) {
                            e.preventDefault();
                            var p = document.createElement('p');
                            p.innerHTML = '<br>';
                            checkItemB.parentNode.replaceChild(p, checkItemB);
                            var rangeB = document.createRange();
                            rangeB.selectNodeContents(p);
                            rangeB.collapse(false);
                            selB.removeAllRanges();
                            selB.addRange(rangeB);
                            if (self.currentDotNetHelper) {
                                self.notifyContentChanged();
                            }
                            return;
                        }
                    }
                }
            }

            // Enter w checkliście (tworzy kolejny punkt lub zamyka listę przy podwójnym Enter)
            if (e.key === 'Enter' && !e.shiftKey) {
                var selE = window.getSelection();
                if (selE && selE.anchorNode) {
                    var checkItem = self.closest(selE.anchorNode, '.notes-checklist-item');
                    if (checkItem) {
                        e.preventDefault();
                        var textSpan = checkItem.querySelector('span');
                        // Jeśli punkt pusty - zamień w zwykły paragraf i zakończ listę
                        if (textSpan && (textSpan.innerText.trim() === '' || textSpan.innerText === '\u00A0')) {
                            var p = document.createElement('p');
                            p.innerHTML = '<br>';
                            checkItem.parentNode.replaceChild(p, checkItem);
                            var rangeP = document.createRange();
                            rangeP.selectNodeContents(p);
                            rangeP.collapse(false);
                            selE.removeAllRanges();
                            selE.addRange(rangeP);
                            if (self.currentDotNetHelper) {
                                self.notifyContentChanged();
                            }
                            return;
                        }

                        // Utwórz nowy punkt checklisty poniżej
                        var newItem = document.createElement('div');
                        newItem.className = 'notes-checklist-item';
                        newItem.innerHTML = '<input type="checkbox" onchange="window.notesEditor.onChecklistToggle(this)"><span>&nbsp;</span>';

                        if (checkItem.nextSibling) {
                            checkItem.parentNode.insertBefore(newItem, checkItem.nextSibling);
                        } else {
                            checkItem.parentNode.appendChild(newItem);
                        }

                        // Ustaw kursor w nowo utworzonym punkcie
                        var span = newItem.querySelector('span');
                        var rangeNew = document.createRange();
                        rangeNew.selectNodeContents(span);
                        rangeNew.collapse(false);
                        selE.removeAllRanges();
                        selE.addRange(rangeNew);

                        if (self.currentDotNetHelper) {
                            self.notifyContentChanged();
                        }
                    }
                }
            }
        };

        // Nasłuchiwanie kliknięć w checkboxy oraz wstawione obrazy (lightbox)
        function handleImageOrCheckboxClick(e) {
            var target = e.target;
            if (!target) return;

            if (target.type === 'checkbox') {
                self.onChecklistToggle(target);
                return;
            }

            var img = target.tagName === 'IMG' ? target : (target.closest ? target.closest('img') : null);
            if (img && (img === editorEl || editorEl.contains(img))) {
                e.preventDefault();
                e.stopPropagation();
                if (self.currentDotNetHelper) {
                    var attId = img.getAttribute('data-attachment-id') || '';
                    var src = img.getAttribute('src') || '';
                    self.currentDotNetHelper.invokeMethodAsync('OnInlineImageClicked', attId, src);
                }
            }
        }

        editorEl.addEventListener('click', handleImageOrCheckboxClick);
        editorEl.addEventListener('dblclick', handleImageOrCheckboxClick);

        // Obsługa dotyku na urządzeniach mobilnych (iOS / iPadOS) wewnątrz contenteditable
        var touchStartX = 0, touchStartY = 0, touchStartTime = 0;
        editorEl.addEventListener('touchstart', function (e) {
            if (e.touches && e.touches.length === 1) {
                touchStartX = e.touches[0].clientX;
                touchStartY = e.touches[0].clientY;
                touchStartTime = Date.now();
            }
        }, { passive: true });

        editorEl.addEventListener('touchend', function (e) {
            self.saveSelection();
            if (e.changedTouches && e.changedTouches.length === 1) {
                var dx = Math.abs(e.changedTouches[0].clientX - touchStartX);
                var dy = Math.abs(e.changedTouches[0].clientY - touchStartY);
                var dt = Date.now() - touchStartTime;
                if (dx < 14 && dy < 14 && dt < 450) {
                    var target = document.elementFromPoint(e.changedTouches[0].clientX, e.changedTouches[0].clientY);
                    var img = target && (target.tagName === 'IMG' ? target : (target.closest ? target.closest('img') : null));
                    if (img && editorEl.contains(img)) {
                        e.preventDefault();
                        if (self.currentDotNetHelper) {
                            var attId = img.getAttribute('data-attachment-id') || '';
                            var src = img.getAttribute('src') || '';
                            self.currentDotNetHelper.invokeMethodAsync('OnInlineImageClicked', attId, src);
                        }
                    }
                }
            }
        });

        // Globalny przechwytywacz kliknięć w fazie capturing dla obrazów w edytorze
        if (!window.__notesGlobalImageListenerAttached) {
            window.__notesGlobalImageListenerAttached = true;
            document.addEventListener('click', function (e) {
                var target = e.target;
                if (!target) return;
                var editor = self.closest(target, '.notes-rich-editor');
                if (!editor) return;

                var img = target.tagName === 'IMG' ? target : (target.closest ? target.closest('img') : null);
                if (img && editor.contains(img)) {
                    e.preventDefault();
                    e.stopPropagation();
                    if (self.currentDotNetHelper) {
                        var attId = img.getAttribute('data-attachment-id') || '';
                        var src = img.getAttribute('src') || '';
                        self.currentDotNetHelper.invokeMethodAsync('OnInlineImageClicked', attId, src);
                    }
                }
            }, true);

            window.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') {
                    var lightboxClose = document.querySelector('.image-lightbox-overlay .lightbox-close-btn');
                    if (lightboxClose) {
                        lightboxClose.click();
                    }
                }
            });
        }

        // Pokoloruj składnię istniejących bloków kodu po zainicjowaniu
        setTimeout(function () {
            self.highlightAllCodeBlocks();
        }, 150);
    },

    /**
     * Sets HTML content and re-triggers code syntax formatting.
     */
    setContent: function (editorEl, html, noteId) {
        if (!editorEl) return;
        this.editorEl = editorEl;
        clearTimeout(this.debounceTimer);
        if (noteId !== undefined && noteId !== null) {
            this.currentNoteId = noteId;
        }
        if (editorEl.innerHTML !== (html || '')) {
            editorEl.innerHTML = html || '';
            this.highlightAllCodeBlocks();
        }
    },

    /**
     * Executes standard contentEditable command.
     */
    exec: function (command, value) {
        document.execCommand(command, false, value || null);
        this.updateToolbarStates();
        this.notifyContentChanged();
    },

    /**
     * Sets current block format to heading tag (H1, H2, P).
     */
    setHeading: function (tag) {
        document.execCommand('formatBlock', false, tag);
        this.updateToolbarStates();
        this.notifyContentChanged();
    },

    /**
     * Toggles text background highlight color (highlighter marker).
     */
    toggleHighlightColor: function () {
        var selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return;

        var range = selection.getRangeAt(0);
        var parentMark = this.closest(range.commonAncestorContainer, 'mark.notes-highlighter');

        if (parentMark) {
            // Usunięcie zakreślenia
            var text = document.createTextNode(parentMark.innerText);
            parentMark.parentNode.replaceChild(text, parentMark);
        } else {
            // Dodanie zakreślenia
            if (!range.collapsed) {
                var mark = document.createElement('mark');
                mark.className = 'notes-highlighter';
                mark.appendChild(range.extractContents());
                range.insertNode(mark);
            } else {
                document.execCommand('hiliteColor', false, '#fef08a');
            }
        }
        this.notifyContentChanged();
    },

    /**
     * Inserts an inline image with data-attachment-id token.
     * Uses robust DOM element creation and saved selection ranges
     * to avoid focus loss or silent failures of deprecated document.execCommand.
     */
    insertInlineImage: function (attachmentId, dataUri, altText) {
        if (!this.editorEl) return;
        this.editorEl.focus();

        var alt = altText || 'Zdjęcie tablicy';
        var p = document.createElement('p');
        var img = document.createElement('img');
        img.setAttribute('data-attachment-id', attachmentId);
        img.setAttribute('src', dataUri);
        img.className = 'note-inline-image';
        img.alt = alt;
        p.appendChild(img);

        var pSpacing = document.createElement('p');
        pSpacing.innerHTML = '<br>';

        var sel = window.getSelection();
        var rangeToUse = null;

        if (this.savedRange && this.editorEl.contains(this.savedRange.commonAncestorContainer)) {
            rangeToUse = this.savedRange;
        } else if (sel && sel.rangeCount > 0 && this.editorEl.contains(sel.getRangeAt(0).commonAncestorContainer)) {
            rangeToUse = sel.getRangeAt(0);
        }

        if (rangeToUse) {
            rangeToUse.deleteContents();
            rangeToUse.insertNode(pSpacing);
            rangeToUse.insertNode(p);

            // Ustaw kursor w nowo utworzonym akapicie poniżej zdjęcia
            var newRange = document.createRange();
            newRange.selectNodeContents(pSpacing);
            newRange.collapse(false);
            if (sel) {
                sel.removeAllRanges();
                sel.addRange(newRange);
            }
            this.savedRange = newRange.cloneRange();
        } else {
            this.editorEl.appendChild(p);
            this.editorEl.appendChild(pSpacing);

            var endRange = document.createRange();
            endRange.selectNodeContents(pSpacing);
            endRange.collapse(false);
            if (sel) {
                sel.removeAllRanges();
                sel.addRange(endRange);
            }
            this.savedRange = endRange.cloneRange();
        }

        try {
            img.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        } catch (e) {}

        this.notifyContentChanged();
    },

    /**
     * Inserts a new interactive checklist item.
     */
    insertChecklist: function () {
        var html = '<div class="notes-checklist-item"><input type="checkbox" onchange="window.notesEditor.onChecklistToggle(this)"><span>Nowe zadanie...</span></div><p><br></p>';
        document.execCommand('insertHTML', false, html);
        this.notifyContentChanged();
    },

    /**
     * Inserts an interactive code card block with language selection and copy button.
     */
    insertCodeBlock: function (language) {
        var lang = language || 'C#';
        var placeholder = '// Napisz lub wklej swój kod ' + lang + ' tutaj...\n';
        if (lang === 'Python') placeholder = '# Napisz lub wklej swój kod Python tutaj...\n';
        if (lang === 'SQL') placeholder = '-- Napisz lub wklej swoje zapytanie SQL tutaj...\nSELECT * FROM studenci;\n';

        var html = '<div class="notes-code-card" contenteditable="false" data-lang="' + lang + '">' +
            '<div class="notes-code-header">' +
            '<div class="code-header-left">' +
            '<span class="code-lang-badge">💻</span>' +
            '<select class="code-lang-select" onchange="window.notesEditor.onCodeLangSelect(this)">' +
            '<option value="C#"' + (lang === 'C#' ? ' selected' : '') + '>C#</option>' +
            '<option value="Python"' + (lang === 'Python' ? ' selected' : '') + '>Python</option>' +
            '<option value="SQL"' + (lang === 'SQL' ? ' selected' : '') + '>SQL</option>' +
            '<option value="JavaScript"' + (lang === 'JavaScript' ? ' selected' : '') + '>JavaScript</option>' +
            '<option value="C++"' + (lang === 'C++' ? ' selected' : '') + '>C++</option>' +
            '<option value="Java"' + (lang === 'Java' ? ' selected' : '') + '>Java</option>' +
            '<option value="HTML"' + (lang === 'HTML' ? ' selected' : '') + '>HTML/XML</option>' +
            '</select>' +
            '</div>' +
            '<div class="code-header-actions">' +
            '<button type="button" class="code-format-btn" onclick="window.notesEditor.highlightBlock(this)" title="Podświetl składnię kodu">🎨 Koloruj</button>' +
            '<button type="button" class="code-copy-btn" onclick="window.notesEditor.copyCode(this)" title="Kopiuj do schowka">Kopiuj</button>' +
            '</div>' +
            '</div>' +
            '<pre><code contenteditable="true" spellcheck="false" onblur="window.notesEditor.onCodeBlur(this)">' + placeholder + '</code></pre>' +
            '</div><p><br></p>';

        document.execCommand('insertHTML', false, html);
        this.notifyContentChanged();
    },

    onCodeLangSelect: function (selectEl) {
        if (!selectEl) return;
        var card = this.closest(selectEl, '.notes-code-card');
        if (!card) return;
        var lang = selectEl.value;
        card.setAttribute('data-lang', lang);
        var codeEl = card.querySelector('code');
        if (codeEl) {
            this.highlightCodeElement(codeEl, lang);
        }
        this.notifyContentChanged();
    },

    onCodeBlur: function (codeEl) {
        if (!codeEl) return;
        var card = this.closest(codeEl, '.notes-code-card');
        var lang = card ? card.getAttribute('data-lang') || 'C#' : 'C#';
        this.highlightCodeElement(codeEl, lang);
        this.notifyContentChanged();
    },

    highlightBlock: function (buttonEl) {
        if (!buttonEl) return;
        var card = this.closest(buttonEl, '.notes-code-card');
        if (!card) return;
        var codeEl = card.querySelector('code');
        var lang = card.getAttribute('data-lang') || 'C#';
        if (codeEl) {
            this.highlightCodeElement(codeEl, lang);
            var orig = buttonEl.innerText;
            buttonEl.innerText = 'Gotowe! ✓';
            buttonEl.style.color = '#34d399';
            setTimeout(function () {
                buttonEl.innerText = orig;
                buttonEl.style.color = '';
            }, 1400);
        }
    },

    highlightAllCodeBlocks: function () {
        if (!this.editorEl) return;
        var cards = this.editorEl.querySelectorAll('.notes-code-card');
        for (var i = 0; i < cards.length; i++) {
            var card = cards[i];
            var lang = card.getAttribute('data-lang') || 'C#';
            var codeEl = card.querySelector('code');
            if (codeEl) {
                this.highlightCodeElement(codeEl, lang);
            }
        }
    },

    /**
     * Tokenizer and syntax highlighter supporting keywords, strings, comments, numbers, and functions.
     */
    highlightCodeElement: function (codeEl, lang) {
        if (!codeEl) return;
        var rawText = codeEl.innerText;
        if (!rawText || rawText.trim() === '') return;

        var tokens = [];
        var placeholderIndex = 0;

        function saveToken(content, cssClass) {
            var key = '___TOK_' + (placeholderIndex++) + '___';
            tokens.push({ key: key, html: '<span class="' + cssClass + '">' + content + '</span>' });
            return key;
        }

        // Krok 1: Bezpieczne kodowanie HTML znaków specjalnych
        var safe = rawText
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');

        // Krok 2: Komentarze i łańcuchy znaków (chronione przed zamianą słów kluczowych)
        if (lang === 'Python') {
            safe = safe.replace(/(#[^\n]*)/g, function (m) { return saveToken(m, 'tok-com'); });
            safe = safe.replace(/("""[\s\S]*?"""|'''[\s\S]*?'''|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')/g, function (m) { return saveToken(m, 'tok-str'); });
        } else if (lang === 'SQL') {
            safe = safe.replace(/(--[^\n]*|\/\*[\s\S]*?\*\/)/g, function (m) { return saveToken(m, 'tok-com'); });
            safe = safe.replace(/('(?:''|[^'])*')/g, function (m) { return saveToken(m, 'tok-str'); });
        } else {
            // C#, Java, JS, C++
            safe = safe.replace(/(\/\/[^\n]*|\/\*[\s\S]*?\*\/)/g, function (m) { return saveToken(m, 'tok-com'); });
            safe = safe.replace(/(@?"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')/g, function (m) { return saveToken(m, 'tok-str'); });
        }

        // Krok 3: Słowa kluczowe (Keywords)
        var kwRegex;
        if (lang === 'Python') {
            kwRegex = /\b(and|as|assert|async|await|break|class|continue|def|del|elif|else|except|False|finally|for|from|global|if|import|in|is|lambda|None|nonlocal|not|or|pass|raise|return|True|try|while|with|yield|self)\b/g;
        } else if (lang === 'SQL') {
            kwRegex = /\b(SELECT|FROM|WHERE|INSERT|INTO|UPDATE|DELETE|JOIN|LEFT|RIGHT|INNER|OUTER|ON|GROUP|BY|ORDER|HAVING|LIMIT|CREATE|TABLE|DROP|ALTER|PRIMARY|KEY|FOREIGN|REFERENCES|AND|OR|NOT|IN|IS|NULL|AS|UNION|ALL|DISTINCT|VALUES|SET|COUNT|SUM|AVG|MAX|MIN|CASE|WHEN|THEN|ELSE|END)\b/gi;
        } else if (lang === 'JavaScript') {
            kwRegex = /\b(const|let|var|function|return|if|else|for|while|do|switch|case|break|continue|default|import|from|export|class|extends|new|this|super|async|await|try|catch|finally|throw|typeof|instanceof|void|delete|in|of|null|undefined|true|false)\b/g;
        } else {
            // C# domyślnie
            kwRegex = /\b(abstract|as|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|virtual|void|volatile|while|async|await|var|record|init|get|set|value|yield|Task|List|Dictionary)\b/g;
        }

        safe = safe.replace(kwRegex, function (m) { return saveToken(m, 'tok-kw'); });

        // Krok 4: Liczby
        safe = safe.replace(/\b(\d+(?:\.\d+)?)\b/g, function (m) { return saveToken(m, 'tok-num'); });

        // Krok 5: Nazwy funkcji wywoływanych
        safe = safe.replace(/\b([a-zA-Z_]\w*)(?=\s*\()/g, function (m) { return saveToken(m, 'tok-fn'); });

        // Krok 6: Przywrócenie wszystkich stokenizowanych fragmentów
        for (var t = 0; t < tokens.length; t++) {
            safe = safe.replace(tokens[t].key, tokens[t].html);
        }

        codeEl.innerHTML = safe;
    },

    onChecklistToggle: function (checkbox) {
        if (!checkbox) return;
        var parent = this.closest(checkbox, '.notes-checklist-item');
        if (parent) {
            if (checkbox.checked) {
                parent.classList.add('checked');
            } else {
                parent.classList.remove('checked');
            }
        }
        this.notifyContentChanged();
    },

    copyCode: function (button) {
        if (!button) return;
        var card = this.closest(button, '.notes-code-card');
        if (!card) return;
        var code = card.querySelector('code');
        if (!code) return;

        var text = code.innerText;
        navigator.clipboard.writeText(text).then(function () {
            var orig = button.innerText;
            button.innerText = 'Skopiowano! ✓';
            button.style.color = '#34d399';
            setTimeout(function () {
                button.innerText = orig;
                button.style.color = '';
            }, 2000);
        });
    },

    updateToolbarStates: function () {
        this.setButtonActive('btn-format-bold', document.queryCommandState('bold'));
        this.setButtonActive('btn-format-italic', document.queryCommandState('italic'));
        this.setButtonActive('btn-format-underline', document.queryCommandState('underline'));
        this.setButtonActive('btn-format-strike', document.queryCommandState('strikeThrough'));
    },

    setButtonActive: function (id, isActive) {
        var el = document.getElementById(id);
        if (!el) return;
        if (isActive) {
            el.classList.add('active');
        } else {
            el.classList.remove('active');
        }
    },

    closest: function (el, selector) {
        var current = el;
        while (current && current !== document) {
            if (current.nodeType === 1 && current.matches(selector)) {
                return current;
            }
            current = current.parentNode;
        }
        return null;
    }
};
