import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
  title: "EF Core Projectables",
  description: "Flexible projection magic for EF Core — use properties and methods directly in your LINQ queries",
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo.svg' }],
    ['meta', { property: 'og:image', content: 'https://projectables.github.io/social.svg' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    ['meta', { name: 'twitter:image', content: 'https://projectables.github.io/social.svg' }],
  ],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Guide', link: '/guide/introduction' },
      { text: 'Reference', link: '/reference/projectable-attribute' },
      { text: 'Advanced', link: '/advanced/how-it-works' },
      { text: 'Recipes', link: '/recipes/computed-properties' },
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Getting Started',
          items: [
            { text: 'Introduction', link: '/guide/introduction' },
            { text: 'Installation', link: '/guide/installation' },
            { text: 'Quick Start', link: '/guide/quickstart' },
          ]
        },
        {
          text: 'Core Concepts',
          items: [
            { text: 'Projectable Properties', link: '/guide/projectable-properties' },
            { text: 'Projectable Methods', link: '/guide/projectable-methods' },
            { text: 'Extension Methods', link: '/guide/extension-methods' },
          ]
        }
      ],
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: '[Projectable] Attribute', link: '/reference/projectable-attribute' },
            { text: 'Compatibility Mode', link: '/reference/compatibility-mode' },
            { text: 'Null-Conditional Rewrite', link: '/reference/null-conditional-rewrite' },
            { text: 'Expand Enum Methods', link: '/reference/expand-enum-methods' },
            { text: 'Use Member Body', link: '/reference/use-member-body' },
            { text: 'Diagnostics', link: '/reference/diagnostics' },
          ]
        }
      ],
      '/advanced/': [
        {
          text: 'Advanced',
          items: [
            { text: 'How It Works', link: '/advanced/how-it-works' },
            { text: 'Query Compiler Pipeline', link: '/advanced/query-compiler-pipeline' },
            { text: 'Block-Bodied Members', link: '/advanced/block-bodied-members' },
            { text: 'Limitations', link: '/advanced/limitations' },
          ]
        }
      ],
      '/recipes/': [
        {
          text: 'Recipes',
          items: [
            { text: 'Computed Entity Properties', link: '/recipes/computed-properties' },
            { text: 'Enum Display Names', link: '/recipes/enum-display-names' },
            { text: 'Nullable Navigation Properties', link: '/recipes/nullable-navigation' },
            { text: 'Reusable Query Filters', link: '/recipes/reusable-query-filters' },
          ]
        }
      ],
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/koenbeuk/EntityFrameworkCore.Projectables' }
    ],

    search: {
      provider: 'local'
    },

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © EntityFrameworkCore.Projectables Contributors'
    }
  }
})
