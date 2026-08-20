import type { SVGProps } from "react";

const Icon = ({
  children,
  ...props
}: SVGProps<SVGSVGElement> & { children: React.ReactNode }) => (
  <svg
    aria-hidden="true"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    {...props}
  >
    {children}
  </svg>
);

export const SparkIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}>
    <path d="m12 3 1.2 4.1L17 9l-3.8 1.9L12 15l-1.2-4.1L7 9l3.8-1.9L12 3Z" />
    <path d="m5 14 .8 2.4L8 17.5l-2.2 1.1L5 21l-.8-2.4L2 17.5l2.2-1.1L5 14Z" />
    <path d="m19 12 .6 1.8 1.7.7-1.7.8L19 17l-.6-1.7-1.7-.8 1.7-.7L19 12Z" />
  </Icon>
);
export const SendIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}><path d="m22 2-7 20-4-9-9-4 20-7Z" /><path d="M22 2 11 13" /></Icon>
);
export const DatabaseIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}><ellipse cx="12" cy="5" rx="8" ry="3" /><path d="M4 5v6c0 1.7 3.6 3 8 3s8-1.3 8-3V5" /><path d="M4 11v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6" /></Icon>
);
export const ChatIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}><path d="M21 15a4 4 0 0 1-4 4H8l-5 3V7a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4v8Z" /></Icon>
);
export const BookIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20V3H6.5A2.5 2.5 0 0 0 4 5.5v14Z" /><path d="M8 7h8M8 11h6" /></Icon>
);
export const InboxIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}><path d="M4 4h16l2 11h-6l-2 3h-4l-2-3H2L4 4Z" /><path d="M4 11h16" /></Icon>
);
export const ChartIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}><path d="M4 20V10M10 20V4M16 20v-7M22 20H2" /></Icon>
);
export const ChevronIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}><path d="m9 18 6-6-6-6" /></Icon>
);
export const ThumbUpIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}><path d="M7 10v11H3V10h4Zm0 9h10a2 2 0 0 0 2-1.6l1.5-7A2 2 0 0 0 18.5 8H14l1-4a2 2 0 0 0-2-2L7 10" /></Icon>
);
export const ThumbDownIcon = (props: SVGProps<SVGSVGElement>) => (
  <Icon {...props}><path d="M7 14V3H3v11h4Zm0-9h10a2 2 0 0 1 2 1.6l1.5 7a2 2 0 0 1-2 2.4H14l1 4a2 2 0 0 1-2 2L7 14" /></Icon>
);
