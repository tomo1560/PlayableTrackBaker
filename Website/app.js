import { baseLayerLuminance, StandardLuminance } from './fluent-web-components.min.js';

const LISTING_URL = "{{ listingInfo.Url | string.escape }}";

const PACKAGES = {
{{~ for package in packages ~}}
  "{{ package.Name | string.escape }}": {
    name: "{{ package.Name | string.escape }}",
    displayName: "{{ if package.DisplayName; package.DisplayName | string.escape; end; }}",
    description: "{{ if package.Description; package.Description | string.escape; end; }}",
    version: "{{ package.Version | string.escape }}",
    author: {
      name: "{{ if package.Author.Name; package.Author.Name | string.escape; end; }}",
      url: "{{ if package.Author.Url; package.Author.Url | string.escape; end; }}",
    },
    dependencies: {
      {{~ for dependency in package.Dependencies ~}}
        "{{ dependency.Name | string.escape }}": "{{ dependency.Version | string.escape }}",
      {{~ end ~}}
    },
    keywords: [
      {{~ for keyword in package.Keywords ~}}
        "{{ keyword | string.escape }}",
      {{~ end ~}}
    ],
    license: "{{ package.License | string.escape }}",
    licensesUrl: "{{ package.LicenseUrl | string.escape }}",
  },
{{~ end ~}}
};

const setTheme = () => {
  const isDarkTheme = () => window.matchMedia("(prefers-color-scheme: dark)").matches;
  if (isDarkTheme()) {
    baseLayerLuminance.setValueFor(document.documentElement, StandardLuminance.DarkMode);
  } else {
    baseLayerLuminance.setValueFor(document.documentElement, StandardLuminance.LightMode);
  }
}

const safeHttpUrl = value => {
  try {
    const url = new URL(value, window.location.href);
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null;
  } catch {
    return null;
  }
};

const safeListingUrl = safeHttpUrl(LISTING_URL);

(() => {
  setTheme();

  document.querySelectorAll('[data-safe-href]').forEach(link => {
    const safeUrl = safeHttpUrl(link.dataset.safeHref);
    if (safeUrl) link.href = safeUrl;
    else link.removeAttribute('href');
  });
  document.querySelectorAll('[data-safe-background-url]').forEach(element => {
    const safeUrl = safeHttpUrl(element.dataset.safeBackgroundUrl);
    if (safeUrl) element.style.backgroundImage = `url(${JSON.stringify(safeUrl)})`;
  });

  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    setTheme();
  });

  const packageGrid = document.getElementById('packageGrid');

  const searchInput = document.getElementById('searchInput');
  searchInput.addEventListener('input', ({ target: { value = '' }}) => {
    const items = packageGrid.querySelectorAll('fluent-data-grid-row[row-type="default"]');
    items.forEach(item => {
      if (value === '') {
        item.style.display = 'grid';
        return;
      }
      if (
        item.dataset?.packageName?.toLowerCase()?.includes(value.toLowerCase()) ||
        item.dataset?.packageId?.toLowerCase()?.includes(value.toLowerCase())
      ) {
        item.style.display = 'grid';
      } else {
        item.style.display = 'none';
      }
    });
  });

  const urlBarHelpButton = document.getElementById('urlBarHelp');
  const addListingToVccHelp = document.getElementById('addListingToVccHelp');
  urlBarHelpButton.addEventListener('click', () => {
    addListingToVccHelp.hidden = false;
  });
  const addListingToVccHelpClose = document.getElementById('addListingToVccHelpClose');
  addListingToVccHelpClose.addEventListener('click', () => {
    addListingToVccHelp.hidden = true;
  });

  const vccListingInfoUrlFieldCopy = document.getElementById('vccListingInfoUrlFieldCopy');
  vccListingInfoUrlFieldCopy.addEventListener('click', () => {
    const vccUrlField = document.getElementById('vccListingInfoUrlField');
    vccUrlField.select();
    navigator.clipboard.writeText(vccUrlField.value);
    vccListingInfoUrlFieldCopy.appearance = 'accent';
    setTimeout(() => {
      vccListingInfoUrlFieldCopy.appearance = 'neutral';
    }, 1000);
  });

  const vccAddRepoButton = document.getElementById('vccAddRepoButton');
  vccAddRepoButton.disabled = !safeListingUrl;
  vccAddRepoButton.addEventListener('click', () => {
    if (safeListingUrl) window.location.assign(`vcc://vpm/addRepo?url=${encodeURIComponent(safeListingUrl)}`);
  });

  const vccUrlFieldCopy = document.getElementById('vccUrlFieldCopy');
  vccUrlFieldCopy.addEventListener('click', () => {
    const vccUrlField = document.getElementById('vccUrlField');
    vccUrlField.select();
    navigator.clipboard.writeText(vccUrlField.value);
    vccUrlFieldCopy.appearance = 'accent';
    setTimeout(() => {
      vccUrlFieldCopy.appearance = 'neutral';
    }, 1000);
  });

  const rowMoreMenu = document.getElementById('rowMoreMenu');
  const rowMoreMenuDownload = rowMoreMenu.querySelector('#rowMoreMenuDownload');
  let selectedPackageUrl = null;
  rowMoreMenuDownload.addEventListener('change', () => {
    const packageUrl = safeHttpUrl(selectedPackageUrl);
    if (packageUrl) window.open(packageUrl, '_blank', 'noopener,noreferrer');
  });
  const hideRowMoreMenu = e => {
    if (rowMoreMenu.contains(e.target)) return;
    document.removeEventListener('click', hideRowMoreMenu);
    rowMoreMenu.hidden = true;
  }

  const rowMenuButtons = document.querySelectorAll('.rowMenuButton');
  rowMenuButtons.forEach(button => {
    button.addEventListener('click', e => {
      const sourceButton = e.currentTarget;
      if (rowMoreMenu?.hidden) {
        rowMoreMenu.style.top = `${e.clientY + sourceButton.clientHeight}px`;
        rowMoreMenu.style.left = `${e.clientX - 120}px`;
        rowMoreMenu.hidden = false;
        selectedPackageUrl = sourceButton.dataset?.packageUrl ?? null;

        setTimeout(() => {
          document.addEventListener('click', hideRowMoreMenu);
        }, 1);
      }
    });
  });

  const packageInfoModal = document.getElementById('packageInfoModal');
  const packageInfoModalClose = document.getElementById('packageInfoModalClose');
  packageInfoModalClose.addEventListener('click', () => {
    packageInfoModal.hidden = true;
  });

  // Fluent dialogs use nested shadow-rooted elements, so we need to use JS to style them
  const modalControl = packageInfoModal.shadowRoot.querySelector('.control');
  modalControl.style.maxHeight = "90%";
  modalControl.style.transition = 'height 0.2s ease-in-out';
  modalControl.style.overflowY = 'hidden';

  const packageInfoName = document.getElementById('packageInfoName');
  const packageInfoId = document.getElementById('packageInfoId');
  const packageInfoVersion = document.getElementById('packageInfoVersion');
  const packageInfoDescription = document.getElementById('packageInfoDescription');
  const packageInfoAuthor = document.getElementById('packageInfoAuthor');
  const packageInfoDependencies = document.getElementById('packageInfoDependencies');
  const packageInfoKeywords = document.getElementById('packageInfoKeywords');
  const packageInfoLicense = document.getElementById('packageInfoLicense');

  const rowAddToVccButtons = document.querySelectorAll('.rowAddToVccButton');
  rowAddToVccButtons.forEach((button) => {
    button.disabled = !safeListingUrl;
    button.addEventListener('click', () => {
      if (safeListingUrl) window.location.assign(`vcc://vpm/addRepo?url=${encodeURIComponent(safeListingUrl)}`);
    });
  });

  const rowPackageInfoButton = document.querySelectorAll('.rowPackageInfoButton');
  rowPackageInfoButton.forEach((button) => {
    button.addEventListener('click', e => {
      const packageId = e.currentTarget.dataset?.packageId;
      const packageInfo = PACKAGES?.[packageId];
      if (!packageInfo) {
        console.error(`Did not find package ${packageId}. Packages available:`, PACKAGES);
        return;
      }

      packageInfoName.textContent = packageInfo.displayName;
      packageInfoId.textContent = packageId;
      packageInfoVersion.textContent = `v${packageInfo.version}`;
      packageInfoDescription.textContent = packageInfo.description;
      packageInfoAuthor.textContent = packageInfo.author.name;
      packageInfoAuthor.href = safeHttpUrl(packageInfo.author.url) ?? '#';

      if ((packageInfo.keywords?.length ?? 0) === 0) {
        packageInfoKeywords.parentElement.classList.add('hidden');
      } else {
        packageInfoKeywords.parentElement.classList.remove('hidden');
        packageInfoKeywords.innerHTML = null;
        packageInfo.keywords.forEach(keyword => {
          const keywordDiv = document.createElement('div');
          keywordDiv.classList.add('me-2', 'mb-2', 'badge');
          keywordDiv.textContent = keyword;
          packageInfoKeywords.appendChild(keywordDiv);
        });
      }

      if (!packageInfo.license?.length && !packageInfo.licensesUrl?.length) {
        packageInfoLicense.parentElement.classList.add('hidden');
      } else {
        packageInfoLicense.parentElement.classList.remove('hidden');
        packageInfoLicense.textContent = packageInfo.license ?? 'See License';
        packageInfoLicense.href = safeHttpUrl(packageInfo.licensesUrl) ?? '#';
      }

      packageInfoDependencies.innerHTML = null;
      Object.entries(packageInfo.dependencies).forEach(([name, version]) => {
        const depRow = document.createElement('li');
        depRow.classList.add('mb-2');
        depRow.textContent = `${name} @ v${version}`;
        packageInfoDependencies.appendChild(depRow);
      });

      packageInfoModal.hidden = false;

      setTimeout(() => {
        const height = packageInfoModal.querySelector('.col').clientHeight;
        modalControl.style.setProperty('--dialog-height', `${height + 14}px`);
      }, 1);
    });
  });

  const packageInfoVccUrlFieldCopy = document.getElementById('packageInfoVccUrlFieldCopy');
  packageInfoVccUrlFieldCopy.addEventListener('click', () => {
    const vccUrlField = document.getElementById('packageInfoVccUrlField');
    vccUrlField.select();
    navigator.clipboard.writeText(vccUrlField.value);
    packageInfoVccUrlFieldCopy.appearance = 'accent';
    setTimeout(() => {
      packageInfoVccUrlFieldCopy.appearance = 'neutral';
    }, 1000);
  });

  const packageInfoListingHelp = document.getElementById('packageInfoListingHelp');
  packageInfoListingHelp.addEventListener('click', () => {
    addListingToVccHelp.hidden = false;
  });
})();
