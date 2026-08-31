const DoIt = {
    // ==========================================
    // AUDIO SOUND EFFECTS ENGINE (Web Audio API)
    // ==========================================
    _audioCtx: null,
    getAudioContext() {
        if (!this._audioCtx) {
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            if (AudioCtx) {
                this._audioCtx = new AudioCtx();
            }
        }
        if (this._audioCtx && this._audioCtx.state === 'suspended') {
            this._audioCtx.resume();
        }
        return this._audioCtx;
    },

    isSoundEnabled() {
        const val = localStorage.getItem('doit_sound_enabled');
        return val === null || val === 'true';
    },

    toggleSound() {
        const current = this.isSoundEnabled();
        const next = !current;
        localStorage.setItem('doit_sound_enabled', next.toString());
        this.updateSoundBtn();
        this.toast(next ? 'Efek Suara Diaktifkan 🔊' : 'Efek Suara Dinonaktifkan 🔇', 'info', 2000);
        if (next) this.playAudio('login');
        return next;
    },

    updateSoundBtn() {
        const onIcon = document.getElementById('soundIconOn');
        const offIcon = document.getElementById('soundIconOff');
        const isEnabled = this.isSoundEnabled();
        if (onIcon) onIcon.style.display = isEnabled ? 'inline-block' : 'none';
        if (offIcon) offIcon.style.display = isEnabled ? 'none' : 'inline-block';
    },

    playAudio(type = 'loading') {
        if (!this.isSoundEnabled()) return;
        try {
            const ctx = this.getAudioContext();
            if (!ctx) return;

            const now = ctx.currentTime;

            if (type === 'login') {
                // Harmonic ascending greeting chime: C5 -> E5 -> G5 -> C6
                const notes = [523.25, 659.25, 783.99, 1046.50];
                notes.forEach((freq, idx) => {
                    const osc = ctx.createOscillator();
                    const gain = ctx.createGain();
                    osc.type = 'sine';
                    osc.frequency.setValueAtTime(freq, now + idx * 0.11);

                    gain.gain.setValueAtTime(0.0001, now + idx * 0.11);
                    gain.gain.exponentialRampToValueAtTime(0.18, now + idx * 0.11 + 0.03);
                    gain.gain.exponentialRampToValueAtTime(0.0001, now + idx * 0.11 + 0.45);

                    osc.connect(gain);
                    gain.connect(ctx.destination);

                    osc.start(now + idx * 0.11);
                    osc.stop(now + idx * 0.11 + 0.5);
                });
            } else if (type === 'send' || type === 'transmit') {
                // High-tech transmission whoosh & telemetry ping
                const oscSweep = ctx.createOscillator();
                const gainSweep = ctx.createGain();
                oscSweep.type = 'triangle';
                oscSweep.frequency.setValueAtTime(320, now);
                oscSweep.frequency.exponentialRampToValueAtTime(1400, now + 0.22);

                gainSweep.gain.setValueAtTime(0.01, now);
                gainSweep.gain.linearRampToValueAtTime(0.16, now + 0.08);
                gainSweep.gain.exponentialRampToValueAtTime(0.001, now + 0.26);

                oscSweep.connect(gainSweep);
                gainSweep.connect(ctx.destination);
                oscSweep.start(now);
                oscSweep.stop(now + 0.28);

                // Ping tone
                const oscPing = ctx.createOscillator();
                const gainPing = ctx.createGain();
                oscPing.type = 'sine';
                oscPing.frequency.setValueAtTime(1318.51, now + 0.18); // E6
                gainPing.gain.setValueAtTime(0.001, now + 0.18);
                gainPing.gain.exponentialRampToValueAtTime(0.18, now + 0.21);
                gainPing.gain.exponentialRampToValueAtTime(0.0001, now + 0.65);

                oscPing.connect(gainPing);
                gainPing.connect(ctx.destination);
                oscPing.start(now + 0.18);
                oscPing.stop(now + 0.68);
            } else if (type === 'loading') {
                // Gentle pulse click / sonar tap
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.setValueAtTime(987.77, now); // B5
                osc.frequency.exponentialRampToValueAtTime(1318.51, now + 0.08); // E6

                gain.gain.setValueAtTime(0.001, now);
                gain.gain.exponentialRampToValueAtTime(0.12, now + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.28);

                osc.connect(gain);
                gain.connect(ctx.destination);
                osc.start(now);
                osc.stop(now + 0.3);
            } else if (type === 'success') {
                // Success harmonic double chime (G5 -> C6)
                [783.99, 1046.50].forEach((freq, idx) => {
                    const osc = ctx.createOscillator();
                    const gain = ctx.createGain();
                    osc.type = 'sine';
                    osc.frequency.setValueAtTime(freq, now + idx * 0.12);

                    gain.gain.setValueAtTime(0.001, now + idx * 0.12);
                    gain.gain.exponentialRampToValueAtTime(0.18, now + idx * 0.12 + 0.02);
                    gain.gain.exponentialRampToValueAtTime(0.0001, now + idx * 0.12 + 0.45);

                    osc.connect(gain);
                    gain.connect(ctx.destination);
                    osc.start(now + idx * 0.12);
                    osc.stop(now + idx * 0.12 + 0.5);
                });
            } else if (type === 'error') {
                // Gentle warning tone (D4 -> B3)
                [293.66, 246.94].forEach((freq, idx) => {
                    const osc = ctx.createOscillator();
                    const gain = ctx.createGain();
                    osc.type = 'triangle';
                    osc.frequency.setValueAtTime(freq, now + idx * 0.14);

                    gain.gain.setValueAtTime(0.001, now + idx * 0.14);
                    gain.gain.exponentialRampToValueAtTime(0.15, now + idx * 0.14 + 0.03);
                    gain.gain.exponentialRampToValueAtTime(0.0001, now + idx * 0.14 + 0.35);

                    osc.connect(gain);
                    gain.connect(ctx.destination);
                    osc.start(now + idx * 0.14);
                    osc.stop(now + idx * 0.14 + 0.4);
                });
            }
        } catch (e) {
            console.warn('Audio playback not permitted or supported:', e);
        }
    },

    toast(message, type = 'info', duration = 4000) {
        const container = document.querySelector('.toast-container') || (() => {
            const el = document.createElement('div');
            el.className = 'toast-container';
            document.body.appendChild(el);
            return el;
        })();

        const icons = { success: '✓', danger: '✕', warning: '⚠', info: 'ℹ' };
        const colors = { success: 'var(--accent-success)', danger: 'var(--accent-danger)', warning: 'var(--accent-warning)', info: 'var(--accent-primary)' };

        if (type === 'success') {
            this.playAudio('success');
        } else if (type === 'danger' || type === 'error') {
            this.playAudio('error');
        }

        const toast = document.createElement('div');
        toast.className = 'toast';
        toast.innerHTML = `
            <span style="color:${colors[type]};font-weight:700;font-size:16px">${icons[type]}</span>
            <span style="flex:1;font-size:13px;color:var(--text-secondary)">${message}</span>
            <button onclick="this.parentElement.remove()" style="background:transparent;border:none;color:var(--text-muted);cursor:pointer;font-size:16px;padding:0;line-height:1">&times;</button>
        `;
        container.appendChild(toast);

        setTimeout(() => {
            toast.classList.add('removing');
            setTimeout(() => toast.remove(), 250);
        }, duration);
    },

    // Modal control
    openModal(id) {
        const el = document.getElementById(id);
        if (el) el.classList.add('active');
    },
    closeModal(id) {
        const el = document.getElementById(id);
        if (el) el.classList.remove('active');
    },

    toggleNotifDropdown(e) {
        if (e) { e.preventDefault(); e.stopPropagation(); }
        const popover = document.getElementById('notifPopover');
        if (popover) {
            const isVisible = popover.style.display === 'flex' || popover.style.display === 'block';
            popover.style.display = isVisible ? 'none' : 'flex';
            if (!isVisible) {
                DoIt.loadNotifications();
            }
        }
    },

    async loadNotifications() {
        const container = document.getElementById('notifListContainer');
        const dot = document.getElementById('notifUnreadDot');
        try {
            const res = await fetch('/Dashboard/GetNotifications');
            const notifs = await res.json();
            
            const unread = notifs.filter(n => !n.isRead);
            if (dot) {
                dot.style.display = unread.length > 0 ? 'inline-block' : 'none';
            }

            if (!container) return;

            if (!notifs || notifs.length === 0) {
                container.innerHTML = '<div style="text-align: center; padding: 24px; color: #94a3b8; font-size: 12px;">Belum ada notifikasi aktivitas.</div>';
                return;
            }

            let html = '';
            notifs.forEach(n => {
                const bg = n.isRead ? '#ffffff' : '#f0fdf4';
                const border = n.isRead ? 'border: 1px solid #f1f5f9;' : 'border: 1px solid #bbf7d0;';
                const typeIcon = n.type === 'ERROR' ? '❌' : (n.type === 'SUCCESS' ? '✅' : (n.type === 'WARNING' ? '⚠️' : 'ℹ️'));
                const actionLink = n.actionUrl ? `<a href="${n.actionUrl}" style="font-size: 11px; color: #2563eb; text-decoration: underline; display: inline-block; margin-top: 4px;">Lihat Dokumen ➔</a>` : '';

                html += `
                    <div style="background: ${bg}; ${border} border-radius: 6px; padding: 10px 12px; margin-bottom: 8px; font-size: 12px;">
                        <div style="display: flex; justify-content: space-between; align-items: start; gap: 6px;">
                            <div style="font-weight: 700; color: #1e293b; display: flex; align-items: center; gap: 4px;">
                                <span>${typeIcon}</span> ${n.title}
                            </div>
                            <span style="font-size: 10px; color: #94a3b8; white-space: nowrap;">${new Date(n.createdAt).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}</span>
                        </div>
                        <div style="color: #475569; margin-top: 3px; line-height: 1.4;">${n.message}</div>
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-top: 4px;">
                            ${actionLink}
                            ${!n.isRead ? `<button onclick="DoIt.markNotifRead(${n.id})" style="background: none; border: none; font-size: 10px; color: #64748b; cursor: pointer;">Tandai Dibaca</button>` : ''}
                        </div>
                    </div>
                `;
            });
            container.innerHTML = html;
        } catch (err) {
            if (container) container.innerHTML = '<div style="color: #ef4444; padding: 12px; font-size: 11px;">Gagal memuat notifikasi.</div>';
        }
    },

    async markNotifRead(id) {
        try {
            await fetch(`/Dashboard/MarkNotifRead?id=${id}`, { method: 'POST' });
            DoIt.loadNotifications();
        } catch (e) {}
    },

    async markAllNotifsRead() {
        try {
            await fetch('/Dashboard/MarkAllNotifsRead', { method: 'POST' });
            DoIt.loadNotifications();
        } catch (e) {}
    },

    initTabs(container) {
        const tabs = container.querySelectorAll('.tab-btn');
        tabs.forEach(tab => {
            tab.addEventListener('click', () => {
                tabs.forEach(t => t.classList.remove('active'));
                container.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));
                tab.classList.add('active');
                const target = document.getElementById(tab.dataset.target);
                if (target) target.classList.add('active');
            });
        });
    },

    // Confirm dialog
    async confirm(message, title = 'Konfirmasi Peringatan') {
        return new Promise(resolve => {
            const modal = document.getElementById('confirmModal');
            if (!modal) { resolve(true); return; }
            const titleEl = modal.querySelector('.confirm-title');
            const msgEl = modal.querySelector('.confirm-message');
            if (titleEl) titleEl.textContent = title;
            if (msgEl) msgEl.textContent = message;
            modal.classList.add('active');
            
            const btnOk = modal.querySelector('.confirm-ok');
            const btnCancels = modal.querySelectorAll('.confirm-cancel');
            
            const cleanup = () => {
                modal.classList.remove('active');
                btnOk.removeEventListener('click', onOk);
                btnCancels.forEach(b => b.removeEventListener('click', onCancel));
            };
            const onOk = () => { cleanup(); resolve(true); };
            const onCancel = () => { cleanup(); resolve(false); };
            
            btnOk.addEventListener('click', onOk);
            btnCancels.forEach(b => b.addEventListener('click', onCancel));
        });
    },

    showLoading(msg = 'Memproses...') {
        let overlay = document.getElementById('suzukiLoadingOverlay');
        if (overlay) {
            overlay.classList.remove('fade-out');
            const bar = document.getElementById('suzukiProgressBar');
            const txt = document.getElementById('suzukiLoadingStatus');
            if (bar) bar.style.width = '70%';
            if (txt) txt.textContent = msg;
        }

        // Trigger contextual audio sound effect
        const lower = (msg || '').toLowerCase();
        if (lower.includes('kirim') || lower.includes('transmisi') || lower.includes('ceisa') || lower.includes('silo')) {
            this.playAudio('send');
        } else if (lower.includes('autentikasi') || lower.includes('login') || lower.includes('akses')) {
            this.playAudio('login');
        } else {
            this.playAudio('loading');
        }
    },
    hideLoading() {
        const overlay = document.getElementById('suzukiLoadingOverlay');
        if (overlay) {
            const bar = document.getElementById('suzukiProgressBar');
            if (bar) bar.style.width = '100%';
            setTimeout(() => {
                overlay.classList.add('fade-out');
            }, 250);
        }
    },

    formatRp(val) {
        return 'Rp ' + Number(val).toLocaleString('id-ID');
    },
    formatNumber(val) {
        return Number(val).toLocaleString('id-ID');
    }
};

document.addEventListener('DOMContentLoaded', () => {
    // Intercept form submissions that have onclick="return confirm(...)" or data-confirm
    document.addEventListener('submit', async (e) => {
        if (e.defaultPrevented) return;
        const form = e.target;
        if (form.dataset.confirmed) return; // Already confirmed via modal

        let confirmMsg = form.dataset.confirm;
        const submitter = e.submitter;
        if (!confirmMsg && submitter) confirmMsg = submitter.dataset.confirm;

        // Auto-extract inline onclick="return confirm('...')" string if present
        if (!confirmMsg && submitter) {
            const onclickAttr = submitter.getAttribute('onclick') || '';
            const match = onclickAttr.match(/confirm\(['"](.*?)['"]\)/);
            if (match && match[1]) {
                confirmMsg = match[1];
                submitter.removeAttribute('onclick'); // Disable native browser confirm popup
            }
        }

        if (!confirmMsg) {
            // Also check form button or inputs with confirm
            const btnWithConfirm = form.querySelector('[onclick*="confirm("]');
            if (btnWithConfirm) {
                const onclickAttr = btnWithConfirm.getAttribute('onclick') || '';
                const match = onclickAttr.match(/confirm\(['"](.*?)['"]\)/);
                if (match && match[1]) {
                    confirmMsg = match[1];
                    btnWithConfirm.removeAttribute('onclick');
                }
            }
        }

        if (confirmMsg) {
            e.preventDefault();
            e.stopPropagation();
            const result = await DoIt.confirm(confirmMsg, 'Peringatan Konfirmasi Aksi');
            if (result) {
                form.dataset.confirmed = 'true';
                if (typeof form.requestSubmit === 'function') {
                    form.requestSubmit(submitter);
                } else {
                    form.submit();
                }
            }
            return;
        }

        // Auto-trigger 3D Suzuki Loading Overlay ONLY on data save, CEISA send, upload & action submits
        if (form.dataset.noLoading) return;

        let msg = 'Memproses data...';
        const action = (form.getAttribute('action') || '').toLowerCase();

        if (action.includes('login')) {
            msg = 'Autentikasi Pengguna & Membuka Akses...';
        } else if (action.includes('ceisa') || action.includes('send') || action.includes('transmit')) {
            msg = 'Mengirim Transmisi Dokumen ke CEISA 4.0...';
        } else if (action.includes('save') || action.includes('create') || action.includes('update') || action.includes('edit') || action.includes('deactivate') || action.includes('delete')) {
            msg = 'Menyimpan & Memperbarui Data Dokumen...';
        } else if (action.includes('upload') || action.includes('silo') || action.includes('excel')) {
            msg = 'Mengunggah & Memproses Sinkronisasi Data...';
        }

        DoIt.showLoading(msg);
    });

    // Close notification popover when clicking outside
    document.addEventListener('click', (e) => {
        const notifWrapper = document.getElementById('notifWrapper');
        const popover = document.getElementById('notifPopover');
        if (popover && notifWrapper && !notifWrapper.contains(e.target)) {
            popover.style.display = 'none';
        }
    });

    // Trigger for buttons with explicit data-loading-msg or .btn-loading
    document.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-loading], .btn-loading');
        if (btn) {
            const msg = btn.getAttribute('data-loading-msg') || 'Memproses Aksi...';
            DoIt.showLoading(msg);
        }
    });

    // Initialize Theme Switcher Icons
    const savedTheme = localStorage.getItem('theme') || 'dark';
    updateThemeIcons(savedTheme);

    const themeBtn = document.getElementById('themeToggleBtn');
    if (themeBtn) {
        themeBtn.addEventListener('click', () => {
            const currentTheme = document.documentElement.getAttribute('data-theme') || 'dark';
            const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-theme', newTheme);
            localStorage.setItem('theme', newTheme);
            updateThemeIcons(newTheme);
        });
    }

    function updateThemeIcons(theme) {
        const sunIcon = document.getElementById('themeIconSun');
        const moonIcon = document.getElementById('themeIconMoon');
        if (sunIcon && moonIcon) {
            if (theme === 'light') {
                sunIcon.style.display = 'none';
                moonIcon.style.display = 'block';
            } else {
                sunIcon.style.display = 'block';
                moonIcon.style.display = 'none';
            }
        }
    }

    document.querySelectorAll('.tabs-nav').forEach(nav => DoIt.initTabs(nav.parentElement));

    document.querySelectorAll('.modal-overlay').forEach(overlay => {
        overlay.addEventListener('click', (e) => {
            if (e.target === overlay) overlay.classList.remove('active');
        });
    });

    // Mobile Sidebar Drawer Toggle & Backdrop
    const sidebar = document.getElementById('sidebar');
    const sidebarToggleBtn = document.getElementById('sidebarToggleBtn');
    const sidebarBackdrop = document.getElementById('sidebarBackdrop');

    function toggleMobileSidebar() {
        if (!sidebar) return;
        const isOpen = sidebar.classList.contains('open');
        if (isOpen) {
            sidebar.classList.remove('open');
            if (sidebarBackdrop) sidebarBackdrop.classList.remove('show');
        } else {
            sidebar.classList.add('open');
            if (sidebarBackdrop) sidebarBackdrop.classList.add('show');
        }
    }

    if (sidebarToggleBtn) {
        sidebarToggleBtn.addEventListener('click', toggleMobileSidebar);
    }
    if (sidebarBackdrop) {
        sidebarBackdrop.addEventListener('click', toggleMobileSidebar);
    }

    // Sidebar Dropdown Toggle Handler
    document.addEventListener('click', (e) => {
        const toggle = e.target.closest('[data-toggle="sidebar-dropdown"]');
        if (toggle) {
            e.preventDefault();
            const targetId = toggle.getAttribute('data-target');
            if (targetId) {
                const targetEl = document.querySelector(targetId);
                if (targetEl) {
                    const isShown = targetEl.classList.contains('show');
                    if (isShown) {
                        targetEl.classList.remove('show');
                        toggle.classList.remove('expanded');
                        toggle.setAttribute('aria-expanded', 'false');
                    } else {
                        targetEl.classList.add('show');
                        toggle.classList.add('expanded');
                        toggle.setAttribute('aria-expanded', 'true');
                    }
                }
            }
        }
    });

    // Auto-expand parent dropdown for active page
    const path = window.location.pathname.toLowerCase();
    document.querySelectorAll('.nav-link').forEach(link => {
        const href = link.getAttribute('href')?.toLowerCase();
        if (href && href !== '/' && path.startsWith(href)) {
            link.classList.add('active');
            const collapse = link.closest('.nav-collapse');
            if (collapse) {
                collapse.classList.add('show');
                const toggle = document.querySelector(`[data-target="#${collapse.id}"]`);
                if (toggle) {
                    toggle.classList.add('expanded');
                    toggle.setAttribute('aria-expanded', 'true');
                }
            }
        }
    });

    const scaleSelect = document.getElementById('displayScaleSelect');
    if (scaleSelect) {
        const savedScale = localStorage.getItem('app-display-scale') || '100';
        scaleSelect.value = savedScale;
    }

    // Setup Sound Effects toggle state
    DoIt.updateSoundBtn();

    // Auto-unlock Web Audio API context on first user gesture
    const unlockAudio = () => {
        DoIt.getAudioContext();
        window.removeEventListener('pointerdown', unlockAudio);
        window.removeEventListener('keydown', unlockAudio);
    };
    window.addEventListener('pointerdown', unlockAudio, { once: true });
    window.addEventListener('keydown', unlockAudio, { once: true });

    const successMsg = document.getElementById('tempSuccess')?.value;
    const errorMsg = document.getElementById('tempError')?.value;
    if (successMsg) DoIt.toast(successMsg, 'success');
    if (errorMsg) DoIt.toast(errorMsg, 'danger');

    // Load initial notifications & set 30s background check
    DoIt.loadNotifications();
    setInterval(() => DoIt.loadNotifications(), 30000);
});

// Display Scale (Zoom) Handler
DoIt.setDisplayScale = function(scale) {
    if (!scale || scale === '100') {
        document.documentElement.removeAttribute('data-scale');
        localStorage.setItem('app-display-scale', '100');
        DoIt.toast('Skala tampilan: 100% (Standar)', 'info', 2000);
    } else {
        document.documentElement.setAttribute('data-scale', scale);
        localStorage.setItem('app-display-scale', scale);
        DoIt.toast(`Skala tampilan: ${scale}% (Fullscreen Laptop)`, 'info', 2000);
    }
    const select = document.getElementById('displayScaleSelect');
    if (select) select.value = scale;
};

