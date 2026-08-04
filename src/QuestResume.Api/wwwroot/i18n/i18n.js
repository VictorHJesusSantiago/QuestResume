



(function () {
  const DEFAULT_LANGUAGE = 'pt-BR';
  const SUPPORTED_LANGUAGES = ['pt-BR', 'en-US'];

  let translations = {};
  let observer = null;
  let debounceTimer = null;

  function getSavedLanguage() {
    const saved = localStorage.getItem('language');
    return SUPPORTED_LANGUAGES.includes(saved) ? saved : DEFAULT_LANGUAGE;
  }

  
  
  
  function resolveKey(key) {
    if (Object.prototype.hasOwnProperty.call(translations, key)) {
      return translations[key];
    }
    const parts = key.split('.');
    let node = translations;
    for (const part of parts) {
      if (node && typeof node === 'object' && Object.prototype.hasOwnProperty.call(node, part)) {
        node = node[part];
      } else {
        return undefined;
      }
    }
    return typeof node === 'string' ? node : undefined;
  }

  function applyI18n() {
    const wasObserving = !!observer;
    if (wasObserving) observer.disconnect();

    document.querySelectorAll('[data-i18n]').forEach((el) => {
      const key = el.getAttribute('data-i18n');
      const value = resolveKey(key);
      if (value !== undefined && el.textContent !== value) {
        el.textContent = value;
      }
    });

    document.querySelectorAll('[data-i18n-placeholder]').forEach((el) => {
      const key = el.getAttribute('data-i18n-placeholder');
      const value = resolveKey(key);
      if (value !== undefined && el.placeholder !== value) {
        el.placeholder = value;
      }
    });

    if (wasObserving) observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  }

  function scheduleApply() {
    if (debounceTimer) return;
    debounceTimer = setTimeout(() => {
      debounceTimer = null;
      applyI18n();
    }, 80);
  }

  function startObserving() {
    if (observer) observer.disconnect();
    observer = new MutationObserver(() => scheduleApply());
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  }

  async function loadLanguage(language) {
    const res = await fetch(`./i18n/${language}.json`);
    if (!res.ok) throw new Error(`Falha ao carregar idioma ${language}`);
    translations = await res.json();
  }

  async function setLanguage(language) {
    const lang = SUPPORTED_LANGUAGES.includes(language) ? language : DEFAULT_LANGUAGE;
    localStorage.setItem('language', lang);
    document.documentElement.setAttribute('lang', lang.toLowerCase());
    await loadLanguage(lang);
    applyI18n();
  }

  window.applyI18n = applyI18n;
  window.setLanguage = setLanguage;
  
  
  
  window.t = function (key, params) {
    let value = resolveKey(key);
    if (value === undefined) return key;
    if (params) {
      for (const [name, val] of Object.entries(params)) {
        value = value.replaceAll(`{${name}}`, String(val));
      }
    }
    return value;
  };

  document.addEventListener('DOMContentLoaded', async () => {
    const language = getSavedLanguage();
    try {
      await loadLanguage(language);
    } catch {
      
      translations = {};
    }
    applyI18n();
    startObserving();

    const select = document.getElementById('languageSelect');
    if (select) {
      select.value = language;
      select.addEventListener('change', () => {
        setLanguage(select.value).catch(() => {
          
        });
      });
    }
  });
})();
