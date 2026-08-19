const DoIt = {
    toast(message, type = 'info', duration = 4000) {
        const container = document.querySelector('.toast-container') || (() => {
            const el = document.createElement('div');
            el.className = 'toast-container';
            document.body.appendChild(el);
            return el;
        })();

        const icons = { success: '✓', danger: '✕', warning: '⚠', info: 'ℹ' };
        const colors = { success: 'var(--accent-success)', danger: 'var(--accent-danger)', warning: 'var(--accent-warning)', info: 'var(--accent-primary)' };

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
            popover.style.display = (popover.style.display === 'block') ? 'none' : 'block';
        }
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

    const successMsg = document.getElementById('tempSuccess')?.value;
    const errorMsg = document.getElementById('tempError')?.value;
    if (successMsg) DoIt.toast(successMsg, 'success');
    if (errorMsg) DoIt.toast(errorMsg, 'danger');
});

